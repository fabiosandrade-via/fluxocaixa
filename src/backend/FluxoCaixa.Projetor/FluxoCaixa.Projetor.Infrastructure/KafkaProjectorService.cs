using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FluxoCaixa.SharedKernel.Events;
using FluxoCaixa.Projetor.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;

namespace FluxoCaixa.Projetor.Infrastructure;

public sealed class KafkaProjectorOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string GroupId { get; set; } = "consolidado-consumer-group";
    public string Topic { get; set; } = "lancamentos-events";
    public string DlqTopic { get; set; } = "lancamentos.dlq";
    public int MaxRetries { get; set; } = 3;
}

public sealed class KafkaProjectorService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly KafkaProjectorOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaProjectorService> _logger;
    private readonly IProducer<string, string> _dlqProducer;

    public KafkaProjectorService(
        IOptions<KafkaProjectorOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<KafkaProjectorService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.BootstrapServers);

        _dlqProducer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            SessionTimeoutMs = 10000
        };

        using var consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(_options.Topic);
        _logger.LogInformation("Projetor consumindo {Topic}.", _options.Topic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var record = consumer.Consume(stoppingToken);
                if (record is null)
                    continue;

                try
                {
                    await ProcessWithRetryAsync(record, stoppingToken);
                    consumer.Commit(record);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex,
                        "Não foi possível projetar nem enviar à DLQ {Topic}[{Partition}]@{Offset}; o offset será repetido.",
                        record.Topic,
                        record.Partition,
                        record.Offset);
                    consumer.Seek(record.TopicPartitionOffset);
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (ConsumeException ex)
            {
                _logger.LogError(ex, "Falha ao consumir mensagem Kafka.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Falha inesperada no Projetor Consolidado.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task ProcessWithRetryAsync(
        ConsumeResult<string, string> record,
        CancellationToken ct)
    {
        Exception? lastError = null;
        var firstFailureAt = DateTime.UtcNow;
        var attempts = Math.Clamp(_options.MaxRetries + 1, 1, 10);

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                EnsureEventIdIsPresent(record.Message.Value);
                var evento = JsonSerializer.Deserialize<LancamentoRegistradoEvent>(record.Message.Value, JsonOptions)
                    ?? throw new JsonException("O payload do evento está vazio.");

                if (evento.EventId == Guid.Empty ||
                    evento.EventType != "lancamento.registrado.v1" ||
                    evento.LancamentoId == Guid.Empty)
                {
                    throw new JsonException("O evento não corresponde ao contrato lancamento.registrado.v1.");
                }

                using var scope = _scopeFactory.CreateScope();
                var projectionWriter = scope.ServiceProvider.GetRequiredService<IProjectionWriter>();
                await projectionWriter.ProcessarEventoAsync(evento, ct);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                _logger.LogWarning(
                    ex,
                    "Tentativa {Attempt}/{MaxAttempts} falhou para {Topic}[{Partition}]@{Offset}.",
                    attempt,
                    attempts,
                    record.Topic,
                    record.Partition,
                    record.Offset);

                if (attempt < attempts)
                {
                    var delaySeconds = attempt switch { 1 => 1, 2 => 5, _ => 30 };
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                }
            }
        }

        if (lastError is null)
            throw new InvalidOperationException("O processamento terminou sem resultado nem erro registrado.");

        var envelope = CreateFailureEnvelope(
            record,
            _options.GroupId,
            attempts,
            firstFailureAt,
            lastError);
        var key = envelope.FalhaId.ToString();
        await _dlqProducer.ProduceAsync(
            _options.DlqTopic,
            new Message<string, string> { Key = key, Value = JsonSerializer.Serialize(envelope, JsonOptions) },
            ct);

        _logger.LogError(
            lastError,
            "Evento {EventId} enviado à DLQ {DlqTopic}; falha {FailureId}.",
            envelope.EventoId,
            _options.DlqTopic,
            envelope.FalhaId);
    }

    private static DlqFailureEnvelope CreateFailureEnvelope(
        ConsumeResult<string, string> record,
        string consumerGroup,
        int attempts,
        DateTime firstFailureAt,
        Exception error)
    {
        var identity = $"{record.Topic}:{record.Partition.Value}:{record.Offset.Value}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var failureId = new Guid(hash.AsSpan(0, 16));
        var eventId = TryReadEventId(record);

        return new DlqFailureEnvelope(
            1,
            failureId,
            eventId,
            new OriginalKafkaMessage(
                record.Topic,
                record.Partition.Value,
                record.Offset.Value,
                record.Message.Key,
                record.Message.Value,
                record.Message.Headers
                    .GroupBy(header => header.Key, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => Convert.ToBase64String(group.Last().GetValueBytes()),
                        StringComparer.Ordinal)),
            new FailureDetails(
                consumerGroup,
                attempts,
                firstFailureAt,
                DateTime.UtcNow,
                error.GetType().Name,
                error.Message.Length <= 500 ? error.Message : error.Message[..500]),
            new ReplayDetails(0, null));
    }

    private static Guid? TryReadEventId(ConsumeResult<string, string> record)
    {
        try
        {
            using var document = JsonDocument.Parse(record.Message.Value);
            if (TryGetPropertyIgnoreCase(document.RootElement, "EventId", out var value) &&
                value.ValueKind == JsonValueKind.String &&
                value.TryGetGuid(out var eventId))
                return eventId;
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void EnsureEventIdIsPresent(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        if (!TryGetPropertyIgnoreCase(document.RootElement, "EventId", out var value) ||
            value.ValueKind != JsonValueKind.String ||
            !value.TryGetGuid(out var eventId) ||
            eventId == Guid.Empty)
        {
            throw new JsonException("O evento não contém um EventId válido.");
        }
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    public override void Dispose()
    {
        _dlqProducer.Dispose();
        base.Dispose();
    }
}

public static class ProjectorInfrastructureExtensions
{
    public static IServiceCollection AddProjectorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<KafkaProjectorOptions>(configuration.GetSection("Kafka"));
        services.AddHostedService<KafkaProjectorService>();
        return services;
    }
}
