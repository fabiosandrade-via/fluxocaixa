namespace FluxoCaixa.SharedKernel.Events;

public abstract record DomainEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OcorridoEm { get; init; } = DateTime.UtcNow;
    public abstract string EventType { get; }
}
