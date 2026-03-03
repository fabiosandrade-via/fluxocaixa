namespace FluxoCaixa.SharedKernel.Events;

public record LancamentoRegistradoEvent : DomainEvent
{
    public override string EventType => "lancamento.registrado.v1";

    public Guid LancamentoId { get; init; }
    public string Tipo { get; init; } = string.Empty;   // "Debito" | "Credito"
    public decimal Valor { get; init; }
    public DateOnly Data { get; init; }
    public string Descricao { get; init; } = string.Empty;
}
