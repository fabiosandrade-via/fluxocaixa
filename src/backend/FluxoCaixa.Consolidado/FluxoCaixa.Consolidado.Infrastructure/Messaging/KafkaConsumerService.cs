using System.Text.Json;
using FluxoCaixa.Consolidado.Application.Interfaces;
using FluxoCaixa.SharedKernel.Events;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxoCaixa.Consolidado.Infrastructure.Messaging;

public sealed class KafkaConsumerOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string GroupId { get; set; } = "consolidado-consumer-group";
    public string TopicLancamentos { get; set; } = "lancamentos-events";
}

/// <summary>
/// BackgroundService que consome eventos do Kafka de forma assíncrona.
/// </summary>
public sealed class KafkaConsumerService : BackgroundService
{
    private readonly KafkaConsumerOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaConsumerService> _logger;

    public KafkaConsumerService(
        IOptions<KafkaConsumerOptions> options,
        IServiceScopeFactory scopeFactory,
        ILogger<KafkaConsumerService> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            SessionTimeoutMs = 10000
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.TopicLancamentos);

        _logger.LogInformation("Kafka consumer iniciado. Tópico: {Topic}", _options.TopicLancamentos);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result is null) continue;

                await ProcessarMensagemAsync(result.Message.Value, stoppingToken);
                consumer.Commit(result);
            }
            catch (ConsumeException ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Erro ao consumir mensagem do Kafka.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        consumer.Close();
        _logger.LogInformation("Kafka consumer encerrado.");
    }

    private async Task ProcessarMensagemAsync(string payload, CancellationToken ct)
    {
        try
        {
            var evento = JsonSerializer.Deserialize<LancamentoRegistradoEvent>(payload);
            if (evento is null)
            {
                _logger.LogWarning("Payload inválido recebido do Kafka: {Payload}", payload);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IConsolidadoService>();

            await service.ProcessarLancamentoAsync(evento.Tipo, evento.Valor, evento.Data, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Falha ao desserializar evento Kafka.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao processar evento de lançamento.");
            throw; // re-throw para não fazer commit do offset
        }
    }
}
