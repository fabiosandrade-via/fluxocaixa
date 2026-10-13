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

            var isCredit = evento.Tipo.Equals("Credito", StringComparison.OrdinalIgnoreCase);
            var isDebit = evento.Tipo.Equals("Debito", StringComparison.OrdinalIgnoreCase);
            if (!isCredit && !isDebit)
                throw new InvalidOperationException($"Tipo de lançamento não suportado: {evento.Tipo}.");

            var now = DateTime.UtcNow;
            var balanceUpdate = Builders<ProjectedDailyBalance>.Update
                .SetOnInsert(balance => balance.Id, dateKey)
                .SetOnInsert(balance => balance.Data, evento.Data)
                .SetOnInsert(balance => balance.CriadoEm, now)
                .Set(balance => balance.AtualizadoEm, now)
                .Inc(balance => balance.TotalCreditos, isCredit ? evento.Valor : 0)
                .Inc(balance => balance.TotalDebitos, isDebit ? evento.Valor : 0);

            await context.Saldos.UpdateOneAsync(
                transaction,
                item => item.Id == dateKey,
                balanceUpdate,
                new UpdateOptions { IsUpsert = true },
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
