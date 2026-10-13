using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Prometheus;

namespace FluxoCaixa.EventPublisher.Infrastructure;

public sealed class EventPublisherOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string BootstrapServers { get; set; } = string.Empty;
    public string Topic { get; set; } = "lancamentos-events";
    public string ReplayCommandTopic { get; set; } = "lancamentos.eventos.republicar";
    public string PublisherName { get; set; } = "lancamentos-kafka";
    public int BatchSize { get; set; } = 100;
    public int PollIntervalMilliseconds { get; set; } = 1000;
}

public sealed class EventPublisherService : BackgroundService
{
    private const string LockKey = "fluxocaixa:lancamentos:event-publisher";
    private static readonly ActivitySource ActivitySource = new("FluxoCaixa.EventPublisher");
    private static readonly Counter PublishedEvents = Metrics.CreateCounter(
        "fluxocaixa_eventpublisher_events_published_total",
        "Eventos confirmados pelo Event Publisher.");
    private static readonly Counter PublishFailures = Metrics.CreateCounter(
        "fluxocaixa_eventpublisher_publish_failures_total",
        "Falhas de publicação ou acesso ao Kafka.");
    private static readonly Gauge PendingOutboxEvents = Metrics.CreateGauge(
        "fluxocaixa_eventpublisher_outbox_pending_events",
        "Eventos pendentes de publicação no Event Store.");
    private static readonly Gauge OldestOutboxEventAgeSeconds = Metrics.CreateGauge(
        "fluxocaixa_eventpublisher_outbox_oldest_age_seconds",
        "Idade do evento mais antigo pendente no Event Store.");
    private readonly EventPublisherOptions _options;
    private readonly ILogger<EventPublisherService> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IProducer<string, string> _producer;

    public EventPublisherService(
        IOptions<EventPublisherOptions> options,
        ILogger<EventPublisherService> logger)
    {
        _options = options.Value;
        _logger = logger;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.BootstrapServers);

        _dataSource = NpgsqlDataSource.Create(_options.ConnectionString);
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000,
            MessageSendMaxRetries = 5,
            RetryBackoffMs = 500
        }).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForEventStoreAsync(stoppingToken);
        await using var connection = await _dataSource.OpenConnectionAsync(stoppingToken);
        await using var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_lock(hashtext(@lock_key))", connection);
        lockCommand.Parameters.AddWithValue("lock_key", LockKey);

        var lockAcquired = await lockCommand.ExecuteScalarAsync(stoppingToken).ConfigureAwait(false);
        if (lockAcquired is not true)
        {
            throw new InvalidOperationException("Outra instância já está executando o Event Publisher.");
        }

        _logger.LogInformation("Event Publisher ativo para o tópico {Topic}.", _options.Topic);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                int published;
                try
                {
                    published = await PublishBatchAsync(connection, stoppingToken);
                    await RefreshOutboxMetricsAsync(connection, stoppingToken);
                }
                catch (KafkaException ex) when (!stoppingToken.IsCancellationRequested)
                {
                    PublishFailures.Inc();
                    _logger.LogWarning(ex, "Kafka indisponível; eventos permanecem pendentes no Event Store e serão tentados novamente.");
                    await RefreshOutboxMetricsAsync(connection, stoppingToken);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                    continue;
                }

                if (published == 0)
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(Math.Max(100, _options.PollIntervalMilliseconds)),
                        stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await using var unlockCommand = new NpgsqlCommand("SELECT pg_advisory_unlock(hashtext(@lock_key))", connection);
            unlockCommand.Parameters.AddWithValue("lock_key", LockKey);
            await unlockCommand.ExecuteScalarAsync(CancellationToken.None);
        }
    }

    private async Task WaitForEventStoreAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await _dataSource.OpenConnectionAsync(ct);
                await using var command = new NpgsqlCommand(
                    "SELECT to_regclass('public.eventos') IS NOT NULL " +
                    "AND to_regclass('public.checkpoints_publicacao') IS NOT NULL",
                    connection);
                var schemaExists = (bool)(await command.ExecuteScalarAsync(ct) ?? false);
                if (schemaExists)
                {
                    command.CommandText =
                        "SELECT EXISTS (SELECT 1 FROM public.checkpoints_publicacao WHERE publicador = @publisher)";
                    command.Parameters.AddWithValue("publisher", _options.PublisherName);
                    if ((bool)(await command.ExecuteScalarAsync(ct) ?? false))
                        return;
                }
            }
            catch (NpgsqlException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Event Store ainda não está pronto; nova verificação em 2 segundos.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }
    }

    private async Task RefreshOutboxMetricsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*),
                   COALESCE(EXTRACT(EPOCH FROM clock_timestamp() - MIN(e.registrado_em)), 0)
            FROM public.eventos e
            JOIN public.checkpoints_publicacao c ON c.publicador = @publisher
            WHERE e.posicao_global > c.ultima_posicao
            """,
            connection);
        command.Parameters.AddWithValue("publisher", _options.PublisherName);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            throw new InvalidOperationException("Não foi possível consultar as métricas do Event Store.");

        PendingOutboxEvents.Set(reader.GetInt64(0));
        OldestOutboxEventAgeSeconds.Set(reader.GetDouble(1));
    }

    private async Task<int> PublishBatchAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        const string selectSql = """
            SELECT e.posicao_global, e.evento_id, e.stream_id, e.tipo_evento,
                   e.versao_schema, e.dados::text
            FROM public.eventos e
            JOIN public.checkpoints_publicacao c
              ON c.publicador = @publisher
            WHERE e.posicao_global > c.ultima_posicao
            ORDER BY e.posicao_global
            LIMIT @batch_size
            """;

        var records = new List<PublishedEvent>();
        await using (var command = new NpgsqlCommand(selectSql, connection))
        {
            command.Parameters.AddWithValue("publisher", _options.PublisherName);
            command.Parameters.AddWithValue("batch_size", Math.Clamp(_options.BatchSize, 1, 1000));
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                records.Add(new PublishedEvent(
                    reader.GetInt64(0),
                    reader.GetGuid(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetInt32(4),
                    reader.GetString(5)));
            }
        }

        foreach (var record in records)
        {
            using var activity = ActivitySource.StartActivity("event.publish", ActivityKind.Producer);
            activity?.SetTag("messaging.system", "kafka");
            activity?.SetTag("messaging.destination.name", _options.Topic);
            activity?.SetTag("messaging.message.id", record.EventId.ToString());
            activity?.SetTag("messaging.kafka.offset", record.Position);
            var message = new Message<string, string>
            {
                Key = record.StreamId,
                Value = record.Payload,
                Headers = new Headers
                {
                    { "event-id", Encoding.UTF8.GetBytes(record.EventId.ToString()) },
                    { "event-type", Encoding.UTF8.GetBytes(record.EventType) },
                    { "event-schema-version", Encoding.UTF8.GetBytes(record.SchemaVersion.ToString()) },
                    { "event-position", Encoding.UTF8.GetBytes(record.Position.ToString()) }
                }
            };
            if (activity?.Id is { } traceParent)
                message.Headers.Add("traceparent", Encoding.UTF8.GetBytes(traceParent));

            await _producer.ProduceAsync(_options.Topic, message, ct);
            await AdvanceCheckpointAsync(connection, record.Position, ct);
            PublishedEvents.Inc();
            _logger.LogInformation(
                "Evento {EventId} na posição {Position} publicado e confirmado.",
                record.EventId,
                record.Position);
        }

        return records.Count;
    }

    private async Task AdvanceCheckpointAsync(
        NpgsqlConnection connection,
        long position,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE public.checkpoints_publicacao
            SET ultima_posicao = @position, atualizado_em = clock_timestamp()
            WHERE publicador = @publisher AND ultima_posicao < @position
            """,
            connection);
        command.Parameters.AddWithValue("position", position);
        command.Parameters.AddWithValue("publisher", _options.PublisherName);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
        {
            throw new InvalidOperationException(
                $"Não foi possível avançar o checkpoint do Event Publisher para {position}.");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        _producer.Flush(TimeSpan.FromSeconds(10));
    }

    public override void Dispose()
    {
        _producer.Dispose();
        _dataSource.Dispose();
        base.Dispose();
    }

    private sealed record PublishedEvent(
        long Position,
        Guid EventId,
        string StreamId,
        string EventType,
        int SchemaVersion,
        string Payload);
}

public static class EventPublisherInfrastructureExtensions
{
    public static IServiceCollection AddEventPublisherInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EventPublisherOptions>(configuration.GetSection("EventPublisher"));
        services.AddHostedService<EventPublisherService>();
        services.AddHostedService<EventReplayCommandService>();
        return services;
    }
}
