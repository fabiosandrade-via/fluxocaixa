namespace FluxoCaixa.Consolidado.Application.UseCases.ObterConsolidado;

public record SaldoConsolidadoDto
{
    public string Id { get; init; } = string.Empty;
    public DateOnly Data { get; init; }
    public decimal TotalCreditos { get; init; }
    public decimal TotalDebitos { get; init; }
    public decimal SaldoFinal { get; init; }
    public DateTime AtualizadoEm { get; init; }
}
