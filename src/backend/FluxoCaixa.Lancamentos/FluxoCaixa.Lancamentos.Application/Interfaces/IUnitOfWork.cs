using FluxoCaixa.SharedKernel.Events;
using FluxoCaixa.Lancamentos.Application.UseCases.RegistrarLancamento;

namespace FluxoCaixa.Lancamentos.Application.Interfaces;

public interface IUnitOfWork
{
    Task<RegistrarLancamentoResponse?> CommitAsync(
        DomainEvent evento,
        string streamId,
        long versaoStream,
        string? idempotencyKey,
        string? requestHash,
        string? responseJson,
        CancellationToken ct = default);

    Task<RegistrarLancamentoResponse?> ObterRespostaIdempotenteAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken ct = default);
}
