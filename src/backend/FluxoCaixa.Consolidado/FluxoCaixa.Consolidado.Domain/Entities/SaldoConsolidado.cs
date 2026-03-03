using FluxoCaixa.SharedKernel.ValueObjects;

namespace FluxoCaixa.Consolidado.Domain.Entities;

/// <summary>
/// Aggregate root do saldo consolidado diário.
/// </summary>
public sealed class SaldoConsolidado
{
    /// <summary>Chave natural do documento — mapeada como _id no MongoDB.</summary>
    public string Id { get; init; } = string.Empty;          // "yyyy-MM-dd"

    public DateOnly Data { get; private set; }
    public Dinheiro TotalCreditos { get; private set; } = Dinheiro.Zero;
    public Dinheiro TotalDebitos { get; private set; } = Dinheiro.Zero;
    public Dinheiro SaldoFinal => TotalCreditos.Subtrair(TotalDebitos);
    public DateTime AtualizadoEm { get; private set; } = DateTime.UtcNow;
    public DateTime CriadoEm { get; private init; } = DateTime.UtcNow;

    // Construtor sem parâmetros exigido pelo driver do MongoDB para desserialização
    public SaldoConsolidado() { }

    public static SaldoConsolidado Criar(DateOnly data) => new()
    {
        Id = data.ToString("yyyy-MM-dd"),
        Data = data
    };

    public void AplicarCredito(decimal valor)
    {
        TotalCreditos = TotalCreditos.Somar(Dinheiro.De(valor));
        AtualizadoEm = DateTime.UtcNow;
    }

    public void AplicarDebito(decimal valor)
    {
        TotalDebitos = TotalDebitos.Somar(Dinheiro.De(valor));
        AtualizadoEm = DateTime.UtcNow;
    }
}
