using FluxoCaixa.Consolidado.Application.Services;
using FluxoCaixa.Consolidado.Domain.Entities;
using FluxoCaixa.Consolidado.Domain.Repositories;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxoCaixa.Consolidado.Tests;

public sealed class ConsolidadoServiceTests
{
    [Fact]
    public async Task ProcessarLancamento_atualiza_saldo_e_invalida_cache()
    {
        var date = new DateOnly(2026, 10, 7);
        var repository = new FakeSaldoRepository();
        var cache = new FakeDistributedCache();
        var service = new ConsolidadoService(
            repository,
            cache,
            NullLogger<ConsolidadoService>.Instance);

        await service.ProcessarLancamentoAsync("Credito", 100m, date);

        var balance = Assert.IsType<SaldoConsolidado>(repository.Saved);
        Assert.Equal(100m, balance.TotalCreditos.Valor);
        Assert.Equal(0m, balance.TotalDebitos.Valor);
        Assert.Contains($"consolidado:{date:yyyy-MM-dd}", cache.RemovedKeys);
    }

    [Fact]
    public async Task ProcessarEvento_delega_evento_ao_repositorio_e_invalida_cache()
    {
        var date = new DateOnly(2026, 10, 8);
        var eventId = Guid.NewGuid();
        var repository = new FakeSaldoRepository();
        var cache = new FakeDistributedCache();
        var service = new ConsolidadoService(
            repository,
            cache,
            NullLogger<ConsolidadoService>.Instance);

        await service.ProcessarEventoAsync(eventId, "Debito", 12.25m, date);

        Assert.Equal(eventId, repository.ProcessedEventId);
        Assert.Equal("Debito", repository.ProcessedEventType);
        Assert.Equal(12.25m, repository.ProcessedEventValue);
        Assert.Equal(date, repository.ProcessedEventDate);
        Assert.Contains($"consolidado:{date:yyyy-MM-dd}", cache.RemovedKeys);
    }

    private sealed class FakeSaldoRepository : ISaldoConsolidadoRepository
    {
        public SaldoConsolidado? Saved { get; private set; }
        public Guid? ProcessedEventId { get; private set; }
        public string? ProcessedEventType { get; private set; }
        public decimal ProcessedEventValue { get; private set; }
        public DateOnly ProcessedEventDate { get; private set; }

        public Task<SaldoConsolidado?> ObterPorDataAsync(DateOnly data, CancellationToken ct = default)
            => Task.FromResult(Saved is { Data: var savedDate } && savedDate == data ? Saved : null);

        public Task SalvarAsync(SaldoConsolidado saldo, CancellationToken ct = default)
        {
            Saved = saldo;
            return Task.CompletedTask;
        }

        public Task ProcessarEventoAsync(Guid eventoId, string tipo, decimal valor, DateOnly data, CancellationToken ct = default)
        {
            ProcessedEventId = eventoId;
            ProcessedEventType = tipo;
            ProcessedEventValue = valor;
            ProcessedEventDate = data;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SaldoConsolidado>> ListarPeriodoAsync(
            DateOnly inicio,
            DateOnly fim,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SaldoConsolidado>>([]);
    }

    private sealed class FakeDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public List<string> RemovedKeys { get; } = [];

        public byte[]? Get(string key) => _values.GetValueOrDefault(key);
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key)
        {
            _values.Remove(key);
            RemovedKeys.Add(key);
        }
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
            => _values[key] = value;
        public Task SetAsync(
            string key,
            byte[] value,
            DistributedCacheEntryOptions options,
            CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }
    }
}
