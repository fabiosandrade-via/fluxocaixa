using FluxoCaixa.Lancamentos.Application.UseCases.ListarLancamentos;
using FluxoCaixa.Lancamentos.Application.UseCases.RegistrarLancamento;

namespace FluxoCaixa.Lancamentos.Application.Interfaces;

/// <summary>
/// Consumidores dependem somente desta interface, nunca da implementação.
/// </summary>
public interface ILancamentoService
{
    Task<RegistrarLancamentoResponse> RegistrarAsync(
        RegistrarLancamentoRequest request,
        CancellationToken ct = default);

    Task<IReadOnlyList<LancamentoDto>> ListarTodosAsync(CancellationToken ct = default);

    Task<IReadOnlyList<LancamentoDto>> ListarPorDataAsync(
        DateOnly data,
        CancellationToken ct = default);
}
