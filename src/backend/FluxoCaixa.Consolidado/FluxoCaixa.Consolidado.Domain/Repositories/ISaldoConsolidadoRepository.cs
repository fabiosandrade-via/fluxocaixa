using FluxoCaixa.Consolidado.Domain.Entities;

namespace FluxoCaixa.Consolidado.Domain.Repositories;

public interface ISaldoConsolidadoRepository
{
    Task<SaldoConsolidado?> ObterPorDataAsync(DateOnly data, CancellationToken ct = default);
    Task SalvarAsync(SaldoConsolidado saldo, CancellationToken ct = default);
    Task<IReadOnlyList<SaldoConsolidado>> ListarPeriodoAsync(DateOnly inicio, DateOnly fim, CancellationToken ct = default);
}
