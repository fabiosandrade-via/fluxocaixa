using FluxoCaixa.Lancamentos.Domain.Entities;

namespace FluxoCaixa.Lancamentos.Domain.Repositories;

public interface ILancamentoRepository
{
    void Adicionar(Lancamento lancamento);
    Task<Lancamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Lancamento>> ListarPorDataAsync(DateOnly data, CancellationToken ct = default);
    Task<IReadOnlyList<Lancamento>> ListarTodosAsync(CancellationToken ct = default);
}
