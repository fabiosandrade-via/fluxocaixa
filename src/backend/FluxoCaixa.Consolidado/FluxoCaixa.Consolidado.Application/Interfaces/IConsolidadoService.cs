using FluxoCaixa.Consolidado.Application.UseCases.ObterConsolidado;

namespace FluxoCaixa.Consolidado.Application.Interfaces;

/// <summary>
/// Contrato do serviço de consolidado diário.
/// </summary>
public interface IConsolidadoService
{
    Task<SaldoConsolidadoDto?> ObterPorDataAsync(DateOnly data, CancellationToken ct = default);
    Task<IReadOnlyList<SaldoConsolidadoDto>> ObterPeriodoAsync(DateOnly inicio, DateOnly fim, CancellationToken ct = default);
    Task ProcessarLancamentoAsync(string tipo, decimal valor, DateOnly data, CancellationToken ct = default);
}
