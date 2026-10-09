using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using FluxoCaixa.Consolidado.Infrastructure.Persistence;
using FluxoCaixa.SharedKernel.Events;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

namespace FluxoCaixa.DlqReprocessor.Infrastructure;

public sealed class DlqReprocessorOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string CommandTopic { get; set; } = "lancamentos.dlq.reprocessar";
    public string DestinationTopic { get; set; } = "lancamentos-events";
    public string GroupId { get; set; } = "lancamentos-dlq-reprocessor";
}

public sealed record DlqReplayCommand(
    Guid ReprocessamentoId,
    Guid FalhaId,
    string Solicitante,
    string Motivo,
    DlqFailureEnvelope Falha);

public sealed class DlqReprocessorService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly DlqReprocessorOptions _options;
    private readonly MongoDbContext _mongo;
    private readonly ILogger<DlqReprocessorService> _logger;
    private readonly IProducer<string, string> _producer;

    public DlqReprocessorService(
        IOptions<DlqReprocessorOptions> options,
        MongoDbContext mongo,
        ILogger<DlqReprocessorService> logger)
    {
        _options = options.Value;
        _mongo = mongo;
        _logger = logger;
        ArgumentException.ThrowIfNullOrWhiteSpace(_options.BootstrapServers);
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true
        }).Build();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false
        }).Build();
        consumer.Subscribe(_options.CommandTopic);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var record = consumer.Consume(stoppingToken);
                if (record is null)
                    continue;

                try
                {
                    var command = JsonSerializer.Deserialize<DlqReplayCommand>(record.Message.Value, JsonOptions)
                        ?? throw new JsonException("Comando de reprocessamento vazio.");
                    await ProcessCommandAsync(command, stoppingToken);
                    consumer.Commit(record);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Falha ao processar comando de reprocessamento; offset será repetido.");
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
                _logger.LogError(ex, "Falha ao consumir comando da DLQ.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }

        consumer.Close();
    }

    private async Task ProcessCommandAsync(DlqReplayCommand command, CancellationToken ct)
    {
        if (command.ReprocessamentoId == Guid.Empty ||
            command.FalhaId == Guid.Empty ||
            command.Falha.FalhaId != command.FalhaId ||
            string.IsNullOrWhiteSpace(command.Solicitante) ||
            string.IsNullOrWhiteSpace(command.Motivo))
        {
            throw new InvalidOperationException("Comando de reprocessamento inválido ou sem auditoria.");
        }

        var collection = _mongo.ReprocessamentosDlq;
        var filter = Builders<ReprocessamentoDlqRecord>.Filter.Eq(
            item => item.ReprocessamentoId,
            command.ReprocessamentoId);
        var existing = await collection.Find(filter).FirstOrDefaultAsync(ct);
        if (existing?.Estado == "publicado")
            return;

        var audit = existing ?? new ReprocessamentoDlqRecord
        {
            ReprocessamentoId = command.ReprocessamentoId,
            FalhaId = command.FalhaId,
            EventoId = command.Falha.EventoId ?? Guid.Empty,
            Solicitante = command.Solicitante,
            Motivo = command.Motivo,
            Estado = "publicando"
        };
        audit.Estado = "publicando";
        await collection.ReplaceOneAsync(filter, audit, new ReplaceOptions { IsUpsert = true }, ct);

        var original = command.Falha.MensagemOriginal;
        var headers = new Headers();
        foreach (var (key, value) in original.Headers)
            headers.Add(key, Convert.FromBase64String(value));
        headers.Add("replay-id", Encoding.UTF8.GetBytes(command.ReprocessamentoId.ToString()));
        headers.Add("origin-dlq-failure-id", Encoding.UTF8.GetBytes(command.FalhaId.ToString()));

        await _producer.ProduceAsync(
            _options.DestinationTopic,
            new Message<string, string>
            {
                Key = original.Chave ??
                      command.Falha.EventoId?.ToString() ??
                      command.Falha.FalhaId.ToString(),
                Value = original.Valor,
                Headers = headers
            },
            ct);

        audit.Estado = "publicado";
        audit.PublicadoEm = DateTime.UtcNow;
        await collection.ReplaceOneAsync(filter, audit, new ReplaceOptions { IsUpsert = true }, ct);
        _logger.LogInformation(
            "Reprocessamento {ReplayId} publicado para evento {EventId}. Solicitante={Requester}",
            command.ReprocessamentoId,
            audit.EventoId,
            command.Solicitante);
    }

    public override void Dispose()
    {
        _producer.Dispose();
        base.Dispose();
    }
}

public static class DlqReprocessorInfrastructureExtensions
{
    public static IServiceCollection AddDlqReprocessorInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<DlqReprocessorOptions>(configuration.GetSection("DlqReprocessor"));
        services.AddHostedService<DlqReprocessorService>();
        return services;
    }
}
