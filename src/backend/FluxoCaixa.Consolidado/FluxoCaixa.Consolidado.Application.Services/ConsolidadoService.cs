using System.Text.Json;
using FluxoCaixa.Consolidado.Application.Interfaces;
using FluxoCaixa.Consolidado.Application.UseCases.ObterConsolidado;
using FluxoCaixa.Consolidado.Domain.Entities;
using FluxoCaixa.Consolidado.Domain.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace FluxoCaixa.Consolidado.Application.Services;

/// <summary>
/// Implementação do serviço de consolidado diário.
/// </summary>
public sealed class ConsolidadoService : IConsolidadoService
{
    private readonly ISaldoConsolidadoRepository _repository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<ConsolidadoService> _logger;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public ConsolidadoService(
        ISaldoConsolidadoRepository repository,
        IDistributedCache cache,
        ILogger<ConsolidadoService> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SaldoConsolidadoDto?> ObterPorDataAsync(DateOnly data, CancellationToken ct = default)
    {
        var cacheKey = $"consolidado:{data:yyyy-MM-dd}";

        // Cache-aside: lê do Redis primeiro
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Cache hit para consolidado {Data}", data);
            return JsonSerializer.Deserialize<SaldoConsolidadoDto>(cached);
        }

        var saldo = await _repository.ObterPorDataAsync(data, ct);
        if (saldo is null) return null;

        var dto = MapToDto(saldo);

        await _cache.SetStringAsync(
            cacheKey,
            JsonSerializer.Serialize(dto),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
            ct);

        return dto;
    }

    public async Task<IReadOnlyList<SaldoConsolidadoDto>> ObterPeriodoAsync(
        DateOnly inicio,
        DateOnly fim,
        CancellationToken ct = default)
    {
        var saldos = await _repository.ListarPeriodoAsync(inicio, fim, ct);
        return saldos.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task ProcessarLancamentoAsync(
        string tipo,
        decimal valor,
        DateOnly data,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Processando lançamento: Tipo={Tipo}, Valor={Valor}, Data={Data}", tipo, valor, data);

        var saldo = await _repository.ObterPorDataAsync(data, ct)
                    ?? SaldoConsolidado.Criar(data);

        if (tipo.Equals("Credito", StringComparison.OrdinalIgnoreCase))
            saldo.AplicarCredito(valor);
        else
            saldo.AplicarDebito(valor);

        await _repository.SalvarAsync(saldo, ct);

        // Invalida cache após atualização
        var cacheKey = $"consolidado:{data:yyyy-MM-dd}";
        await _cache.RemoveAsync(cacheKey, ct);

        _logger.LogInformation("Saldo consolidado de {Data} atualizado. SaldoFinal={Saldo}", data, saldo.SaldoFinal.Valor);
    }

    private static SaldoConsolidadoDto MapToDto(SaldoConsolidado s) => new()
    {
        Id = s.Id,
        Data = s.Data,
        TotalCreditos = s.TotalCreditos.Valor,
        TotalDebitos = s.TotalDebitos.Valor,
        SaldoFinal = s.SaldoFinal.Valor,
        AtualizadoEm = s.AtualizadoEm
    };
}
