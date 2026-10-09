using FluxoCaixa.Consolidado.Domain.Entities;
using FluxoCaixa.Consolidado.Domain.Repositories;
using FluxoCaixa.Consolidado.Infrastructure.Persistence;
using MongoDB.Driver;

namespace FluxoCaixa.Consolidado.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositório MongoDB para SaldoConsolidado.
/// </summary>
public sealed class SaldoConsolidadoRepository : ISaldoConsolidadoRepository
{
    private readonly MongoDbContext _context;
    private readonly IMongoCollection<SaldoConsolidado> _collection;

    public SaldoConsolidadoRepository(MongoDbContext context)
    {
        _context = context;
        _collection = context.SaldosConsolidados;
    }

    public async Task<SaldoConsolidado?> ObterPorDataAsync(DateOnly data, CancellationToken ct = default)
    {
        var id = data.ToString("yyyy-MM-dd");
        var resultado = await _collection
            .Find(s => s.Id == id)
            .FirstOrDefaultAsync(ct);

        return resultado;
    }

    public async Task SalvarAsync(SaldoConsolidado saldo, CancellationToken ct = default)
    {
        var filtro = Builders<SaldoConsolidado>.Filter.Eq(s => s.Id, saldo.Id);

        await _collection.ReplaceOneAsync(
            filtro,
            saldo,
            new ReplaceOptions { IsUpsert = true },
            ct
        );
    }

    public async Task ProcessarEventoAsync(
        Guid eventoId,
        string tipo,
        decimal valor,
        DateOnly data,
        CancellationToken ct = default)
    {
        using var session = await _context.Client.StartSessionAsync(cancellationToken: ct);
        var eventFilter = Builders<EventoProcessadoRecord>.Filter.And(
            Builders<EventoProcessadoRecord>.Filter.Eq(e => e.Projetor, "consolidado"),
            Builders<EventoProcessadoRecord>.Filter.Eq(e => e.EventoId, eventoId));

        await session.WithTransactionAsync(async (transactionSession, token) =>
        {
            if (await _context.EventosProcessados
                    .Find(transactionSession, eventFilter)
                    .AnyAsync(token))
            {
                return true;
            }

            var saldo = await _collection
                .Find(transactionSession, s => s.Id == data.ToString("yyyy-MM-dd"))
                .FirstOrDefaultAsync(token)
                ?? SaldoConsolidado.Criar(data);

            if (tipo.Equals("Credito", StringComparison.OrdinalIgnoreCase))
                saldo.AplicarCredito(valor);
            else if (tipo.Equals("Debito", StringComparison.OrdinalIgnoreCase))
                saldo.AplicarDebito(valor);
            else
                throw new InvalidOperationException($"Tipo de lançamento não suportado: {tipo}.");

            await _collection.ReplaceOneAsync(
                transactionSession,
                s => s.Id == saldo.Id,
                saldo,
                new ReplaceOptions { IsUpsert = true },
                token);
            await _context.EventosProcessados.InsertOneAsync(
                transactionSession,
                new EventoProcessadoRecord
                {
                    Projetor = "consolidado",
                    EventoId = eventoId,
                    ProcessadoEm = DateTime.UtcNow
                },
                cancellationToken: token);

            return true;
        }, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<SaldoConsolidado>> ListarPeriodoAsync(
        DateOnly inicio, DateOnly fim, CancellationToken ct = default)
    {
        var idInicio = inicio.ToString("yyyy-MM-dd");
        var idFim = fim.ToString("yyyy-MM-dd");

        var filtro = Builders<SaldoConsolidado>.Filter.And(
            Builders<SaldoConsolidado>.Filter.Gte(s => s.Id, idInicio),
            Builders<SaldoConsolidado>.Filter.Lte(s => s.Id, idFim)
        );

        var resultado = await _collection
            .Find(filtro)
            .SortBy(s => s.Id)
            .ToListAsync(ct);

        return resultado.AsReadOnly();
    }
}
