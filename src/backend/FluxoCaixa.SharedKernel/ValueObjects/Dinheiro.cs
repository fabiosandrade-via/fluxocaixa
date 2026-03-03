namespace FluxoCaixa.SharedKernel.ValueObjects;

public record Dinheiro
{
    public decimal Valor { get; }

    private Dinheiro(decimal valor) => Valor = valor;

    public static Dinheiro De(decimal valor)
    {
        if (valor < 0)
            throw new ArgumentOutOfRangeException(nameof(valor), "Valor monetário não pode ser negativo.");

        return new Dinheiro(Math.Round(valor, 2));
    }

    public static Dinheiro Zero => new(0m);

    public Dinheiro Somar(Dinheiro outro) => new(Valor + outro.Valor);
    public Dinheiro Subtrair(Dinheiro outro) => new(Valor - outro.Valor);

    public override string ToString() => Valor.ToString("C2");
}
