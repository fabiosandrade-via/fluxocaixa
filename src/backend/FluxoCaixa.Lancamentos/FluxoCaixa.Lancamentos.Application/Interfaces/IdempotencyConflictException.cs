namespace FluxoCaixa.Lancamentos.Application.Interfaces;

public sealed class IdempotencyConflictException : Exception
{
    public IdempotencyConflictException(string message) : base(message) { }
}
