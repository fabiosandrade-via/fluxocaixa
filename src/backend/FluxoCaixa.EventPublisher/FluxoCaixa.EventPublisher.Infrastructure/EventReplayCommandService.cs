using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FluxoCaixa.EventPublisher.Infrastructure;

public sealed record EventReplayCommand(
    Guid ReplayId,
    Guid EventId,
    string RequestedBy,
    string Reason);

public sealed class EventReplayCommandService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string SupportedEventType = "lancamento.registrado.v1";
    private readonly EventPublisherOptions _options;
    private readonly ILogger<EventReplayCommandService> _logger;
    private readonly NpgsqlDataSource _dataSource;
    private readonly IProducer<string, string> _producer;

    public EventReplayCommandService(
        IOptions<EventPublisherOptions> options,
        ILogger<EventReplayCommandService> logger)
    {
        _options = options.Value;
        _logger = logger;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.BootstrapServers);
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.ReplayCommandTopic);

        _dataSource = NpgsqlDataSource.Create(_options.ConnectionString);
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 5,
            RetryBackoffMs = 500
        }).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EnsureAuditTableAsync(stoppingToken);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = "lancamentos-event-replay-commands",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        }).Build();
        consumer.Subscribe(_options.ReplayCommandTopic);
        _logger.LogInformation(
            "Event Publisher aguardando comandos de republicação em {Topic}.",
            _options.ReplayCommandTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var record = consumer.Consume(stoppingToken);
                if (record is null)
                    continue;

                try
                {
                    var command = JsonSerializer.Deserialize<EventReplayCommand>(
                        record.Message.Value,
                        JsonOptions);
                    if (command is null)
                        throw new ReplayRequestException("Comando de republicação vazio.");

                    await ReplayAsync(command, stoppingToken);
                    consumer.Commit(record);
                }
                catch (Exception ex) when (ex is ReplayRequestException or JsonException)
                {
                    _logger.LogError(
                        ex,
                        "Comando inválido em {Topic}[{Partition}]@{Offset}; será descartado.",
                        record.Topic,
                        record.Partition,
                        record.Offset);
                    consumer.Commit(record);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(
                        ex,
                        "Falha ao republicar evento solicitado; o comando será repetido.");
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
                _logger.LogError(ex, "Falha ao consumir comando de republicação.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task ReplayAsync(EventReplayCommand request, CancellationToken ct)
    {
        Validate(request);

        await using var connection = await _dataSource.OpenConnectionAsync(ct);
        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO public.solicitacoes_republicacao_eventos
                (replay_id, evento_id, solicitante, motivo, estado)
            VALUES (@replay_id, @evento_id, @requester, @reason, 'solicitado')
            ON CONFLICT (replay_id) DO NOTHING
            """,
            connection))
        {
            insert.Parameters.AddWithValue("replay_id", request.ReplayId);
            insert.Parameters.AddWithValue("evento_id", request.EventId);
            insert.Parameters.AddWithValue("requester", request.RequestedBy.Trim());
            insert.Parameters.AddWithValue("reason", request.Reason.Trim());
            await insert.ExecuteNonQueryAsync(ct);
        }

        var existing = await ReadAuditAsync(connection, request.ReplayId, ct);
        if (existing is null)
            throw new InvalidOperationException("Não foi possível registrar a solicitação de republicação.");

        if (existing.EventId != request.EventId ||
            !string.Equals(existing.RequestedBy, request.RequestedBy.Trim(), StringComparison.Ordinal) ||
            !string.Equals(existing.Reason, request.Reason.Trim(), StringComparison.Ordinal))
        {
            throw new ReplayRequestException(
                "O ReplayId já foi utilizado com dados diferentes; gere um novo GUID.");
        }

        if (existing.State == "publicado")
        {
            _logger.LogInformation(
                "Solicitação {ReplayId} já publicada; nenhum evento duplicado foi enviado.",
                request.ReplayId);
            return;
        }

        await SetAuditStateAsync(connection, request.ReplayId, "publicando", null, ct);
        var eventRecord = await ReadEventAsync(connection, request.EventId, ct);
        if (eventRecord is null)
        {
            await SetAuditStateAsync(
                connection,
                request.ReplayId,
                "falha",
                "Evento não encontrado em public.eventos.",
                ct);
            throw new ReplayRequestException($"Evento {request.EventId} não encontrado em public.eventos.");
        }

        if (!string.Equals(eventRecord.EventType, SupportedEventType, StringComparison.Ordinal) ||
            eventRecord.SchemaVersion != 1 ||
            eventRecord.LancamentoId is null)
        {
            var error = $"Evento não suportado ou sem lançamento de origem: {eventRecord.EventType} v{eventRecord.SchemaVersion}.";
            await SetAuditStateAsync(connection, request.ReplayId, "falha", error, ct);
            throw new ReplayRequestException(error);
        }

        var payload = JsonSerializer.Serialize(new LancamentoRegistradoPayload(
            eventRecord.EventId,
            eventRecord.EventType,
            eventRecord.OccurredAt,
            eventRecord.LancamentoId.Value,
            eventRecord.Type!,
            eventRecord.Amount!.Value,
            eventRecord.Date!.Value,
            eventRecord.Description!));

        var headers = new Headers
        {
            { "event-id", Encoding.UTF8.GetBytes(eventRecord.EventId.ToString()) },
            { "event-type", Encoding.UTF8.GetBytes(eventRecord.EventType) },
            { "event-schema-version", Encoding.UTF8.GetBytes(eventRecord.SchemaVersion.ToString()) },
            { "event-position", Encoding.UTF8.GetBytes(eventRecord.Position.ToString()) },
            { "replay-id", Encoding.UTF8.GetBytes(request.ReplayId.ToString()) },
            { "replay-requester", Encoding.UTF8.GetBytes(request.RequestedBy.Trim()) }
        };

        await _producer.ProduceAsync(
            _options.Topic,
            new Message<string, string>
            {
                Key = eventRecord.StreamId,
                Value = payload,
                Headers = headers
            },
            ct);

        await SetAuditStateAsync(connection, request.ReplayId, "publicado", null, ct);
        _logger.LogInformation(
            "Evento {EventId}, posição {Position}, republicado por solicitação {ReplayId}. Solicitante={Requester}",
            eventRecord.EventId,
            eventRecord.Position,
            request.ReplayId,
            request.RequestedBy);
    }

    private async Task EnsureAuditTableAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var connection = await _dataSource.OpenConnectionAsync(ct);
                await using var command = new NpgsqlCommand(
                    """
                    CREATE TABLE IF NOT EXISTS public.solicitacoes_republicacao_eventos (
                        replay_id uuid PRIMARY KEY,
                        evento_id uuid NOT NULL,
                        solicitante text NOT NULL,
                        motivo text NOT NULL,
                        estado text NOT NULL CHECK (estado IN ('solicitado', 'publicando', 'publicado', 'falha')),
                        solicitado_em timestamptz NOT NULL DEFAULT clock_timestamp(),
                        publicado_em timestamptz NULL,
                        erro text NULL
                    )
                    """,
                    connection);
                await command.ExecuteNonQueryAsync(ct);
                return;
            }
            catch (NpgsqlException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "PostgreSQL indisponível para inicializar a auditoria de republicação.");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    private static async Task<ReplayAudit?> ReadAuditAsync(
        NpgsqlConnection connection,
        Guid replayId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT evento_id, solicitante, motivo, estado
            FROM public.solicitacoes_republicacao_eventos
            WHERE replay_id = @replay_id
            """,
            connection);
        command.Parameters.AddWithValue("replay_id", replayId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        return new ReplayAudit(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
    }

    private static async Task<EventReplayRecord?> ReadEventAsync(
        NpgsqlConnection connection,
        Guid eventId,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT e.posicao_global, e.evento_id, e.stream_id, e.tipo_evento, e.versao_schema,
                   e.ocorrido_em, l.id, l.tipo, l.valor, l.data, l.descricao
            FROM public.eventos e
            LEFT JOIN public.lancamentos l
              ON l.id = CASE
                    WHEN e.stream_id ~ '^lancamento:[0-9a-fA-F-]{36}$'
                    THEN substring(e.stream_id FROM 12)::uuid
                    ELSE NULL
                 END
            WHERE e.evento_id = @event_id
            """,
            connection);
        command.Parameters.AddWithValue("event_id", eventId);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        return new EventReplayRecord(
            reader.GetInt64(0),
            reader.GetGuid(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetInt32(4),
            reader.GetDateTime(5),
            reader.IsDBNull(6) ? null : reader.GetGuid(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetDecimal(8),
            reader.IsDBNull(9) ? null : reader.GetFieldValue<DateOnly>(9),
            reader.IsDBNull(10) ? null : reader.GetString(10));
    }

    private static async Task SetAuditStateAsync(
        NpgsqlConnection connection,
        Guid replayId,
        string state,
        string? error,
        CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE public.solicitacoes_republicacao_eventos
            SET estado = @state,
                erro = @error,
                publicado_em = CASE WHEN @state = 'publicado' THEN clock_timestamp() ELSE publicado_em END
            WHERE replay_id = @replay_id
            """,
            connection);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.Add("error", NpgsqlTypes.NpgsqlDbType.Text).Value = (object?)error ?? DBNull.Value;
        command.Parameters.AddWithValue("replay_id", replayId);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new InvalidOperationException($"Não foi possível atualizar a auditoria da solicitação {replayId}.");
    }

    private static void Validate(EventReplayCommand request)
    {
        if (request.ReplayId == Guid.Empty || request.EventId == Guid.Empty)
            throw new ReplayRequestException("ReplayId e EventId devem ser GUIDs válidos.");
        if (string.IsNullOrWhiteSpace(request.RequestedBy) || request.RequestedBy.Length > 128)
            throw new ReplayRequestException("RequestedBy é obrigatório e deve ter até 128 caracteres.");
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            throw new ReplayRequestException("Reason é obrigatório e deve ter até 500 caracteres.");
    }

    public override void Dispose()
    {
        _producer.Dispose();
        _dataSource.Dispose();
        base.Dispose();
    }

    private sealed record ReplayAudit(Guid EventId, string RequestedBy, string Reason, string State);

    private sealed record EventReplayRecord(
        long Position,
        Guid EventId,
        string StreamId,
        string EventType,
        int SchemaVersion,
        DateTime OccurredAt,
        Guid? LancamentoId,
        string? Type,
        decimal? Amount,
        DateOnly? Date,
        string? Description);

    private sealed record LancamentoRegistradoPayload(
        Guid EventId,
        string EventType,
        DateTime OcorridoEm,
        Guid LancamentoId,
        string Tipo,
        decimal Valor,
        DateOnly Data,
        string Descricao);

    private sealed class ReplayRequestException(string message) : Exception(message);
}
