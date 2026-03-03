using System.Text.Json;
using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.SharedKernel.Events;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FluxoCaixa.Lancamentos.Infrastructure.Messaging;

public sealed class KafkaOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string TopicLancamentos { get; set; } = "lancamentos-events";
}

/// <summary>
/// Publicador de eventos via Kafka — implementação de IEventPublisher.
/// </summary>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(
        IOptions<KafkaOptions> options,
        ILogger<KafkaEventPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageSendMaxRetries = 5,
            RetryBackoffMs = 500
        };

        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublicarAsync<TEvent>(TEvent evento, CancellationToken ct = default)
        where TEvent : DomainEvent
    {
        var payload = JsonSerializer.Serialize(evento);

        var message = new Message<string, string>
        {
            Key = evento.EventId.ToString(),
            Value = payload,
            Headers = new Headers
            {
                { "event-type", System.Text.Encoding.UTF8.GetBytes(evento.EventType) }
            }
        };

        try
        {
            var result = await _producer.ProduceAsync(_options.TopicLancamentos, message, ct);
            _logger.LogInformation(
                "Evento {EventType} publicado no Kafka. Partition={Partition}, Offset={Offset}",
                evento.EventType, result.Partition, result.Offset);
        }
        catch (ProduceException<string, string> ex)
        {
            _logger.LogError(ex, "Falha ao publicar evento {EventType} no Kafka.", evento.EventType);
            throw;
        }
    }

    public void Dispose() => _producer.Dispose();
}
