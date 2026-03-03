namespace FluxoCaixa.Lancamentos.Application.UseCases.ListarLancamentos;

public record LancamentoDto
{
    public Guid Id { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public decimal Valor { get; init; }
    public DateOnly Data { get; init; }
    public string Descricao { get; init; } = string.Empty;
    public DateTime CriadoEm { get; init; }
}
