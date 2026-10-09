using System.ComponentModel.DataAnnotations;

namespace FluxoCaixa.Lancamentos.Application.UseCases.RegistrarLancamento;

public record RegistrarLancamentoRequest
{
    [Required]
    [RegularExpression("^(Debito|Credito)$", ErrorMessage = "Tipo deve ser 'Debito' ou 'Credito'.")]
    public string Tipo { get; init; } = string.Empty;

    [Required]
    [Range(0.01, double.MaxValue, ErrorMessage = "Valor deve ser maior que zero.")]
    public decimal Valor { get; init; }

    [Required]
    public DateOnly Data { get; init; }

    [Required]
    [MaxLength(250)]
    public string Descricao { get; init; } = string.Empty;
}

public record RegistrarLancamentoResponse
{
    public Guid Id { get; init; }
    public string Tipo { get; init; } = string.Empty;
    public decimal Valor { get; init; }
    public DateOnly Data { get; init; }
    public string Descricao { get; init; } = string.Empty;
    public DateTime CriadoEm { get; init; }
}
