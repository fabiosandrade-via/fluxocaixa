using FluxoCaixa.Lancamentos.Domain.Entities;

namespace FluxoCaixa.Lancamentos.Domain.Repositories;

public interface ILancamentoRepository
{
    Task AdicionarAsync(Lancamento lancamento, CancellationToken ct = default);
    Task<Lancamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Lancamento>> ListarPorDataAsync(DateOnly data, CancellationToken ct = default);
    Task<IReadOnlyList<Lancamento>> ListarTodosAsync(CancellationToken ct = default);
}
