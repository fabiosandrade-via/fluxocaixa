using FluxoCaixa.SharedKernel.Events;
using Microsoft.Extensions.Caching.Distributed;
using MongoDB.Driver;

namespace FluxoCaixa.Projetor.Infrastructure.Persistence;

public interface IProjectionWriter
{
    Task ProcessarEventoAsync(LancamentoRegistradoEvent evento, CancellationToken ct);
}

public sealed class MongoProjectionWriter(
    ProjectorMongoContext context,
    IDistributedCache cache) : IProjectionWriter
{
    private const string ProjectorName = "consolidado";

    public async Task ProcessarEventoAsync(LancamentoRegistradoEvent evento, CancellationToken ct)
    {
        using var session = await context.Client.StartSessionAsync(cancellationToken: ct);
        var eventFilter = Builders<ProjectedEventRecord>.Filter.And(
            Builders<ProjectedEventRecord>.Filter.Eq(record => record.Projetor, ProjectorName),
            Builders<ProjectedEventRecord>.Filter.Eq(record => record.EventoId, evento.EventId));
        var dateKey = evento.Data.ToString("yyyy-MM-dd");

        await session.WithTransactionAsync(async (transaction, token) =>
        {
            if (await context.ProcessedEvents
                    .Find(transaction, eventFilter)
                    .AnyAsync(token))
                return true;

            var balance = await context.Saldos
                .Find(transaction, item => item.Id == dateKey)
                .FirstOrDefaultAsync(token)
                ?? new ProjectedDailyBalance
                {
                    Id = dateKey,
                    Data = evento.Data,
                    CriadoEm = DateTime.UtcNow,
                    AtualizadoEm = DateTime.UtcNow
                };

            if (evento.Tipo.Equals("Credito", StringComparison.OrdinalIgnoreCase))
                balance.TotalCreditos += evento.Valor;
            else if (evento.Tipo.Equals("Debito", StringComparison.OrdinalIgnoreCase))
                balance.TotalDebitos += evento.Valor;
            else
                throw new InvalidOperationException($"Tipo de lançamento não suportado: {evento.Tipo}.");

            balance.AtualizadoEm = DateTime.UtcNow;
            await context.Saldos.ReplaceOneAsync(
                transaction,
                item => item.Id == dateKey,
                balance,
                new ReplaceOptions { IsUpsert = true },
                token);
            await context.ProcessedEvents.InsertOneAsync(
                transaction,
                new ProjectedEventRecord
                {
                    Projetor = ProjectorName,
                    EventoId = evento.EventId,
                    ProcessadoEm = DateTime.UtcNow
                },
                cancellationToken: token);

            return true;
        }, cancellationToken: ct);

        await cache.RemoveAsync($"consolidado:{dateKey}", ct);
    }
}
