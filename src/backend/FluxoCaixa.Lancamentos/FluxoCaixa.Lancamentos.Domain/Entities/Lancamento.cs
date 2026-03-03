using FluxoCaixa.SharedKernel;
using FluxoCaixa.SharedKernel.ValueObjects;

namespace FluxoCaixa.Lancamentos.Domain.Entities;

public enum TipoLancamento { Debito, Credito }

public sealed class Lancamento : Entity
{
    public TipoLancamento Tipo { get; private set; }
    public Dinheiro Valor { get; private set; } = Dinheiro.Zero;
    public DateOnly Data { get; private set; }
    public string Descricao { get; private set; } = string.Empty;

    private Lancamento() { }  // EF Core

    public static Lancamento Criar(
        TipoLancamento tipo,
        decimal valor,
        DateOnly data,
        string descricao)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(descricao, nameof(descricao));

        return new Lancamento
        {
            Tipo = tipo,
            Valor = Dinheiro.De(valor),
            Data = data,
            Descricao = descricao.Trim()
        };
    }
}
