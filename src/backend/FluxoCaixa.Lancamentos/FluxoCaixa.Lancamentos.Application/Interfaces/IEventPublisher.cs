using FluxoCaixa.SharedKernel.Events;

namespace FluxoCaixa.Lancamentos.Application.Interfaces;

public interface IEventPublisher
{
    Task PublicarAsync<TEvent>(TEvent evento, CancellationToken ct = default)
        where TEvent : DomainEvent;
}
