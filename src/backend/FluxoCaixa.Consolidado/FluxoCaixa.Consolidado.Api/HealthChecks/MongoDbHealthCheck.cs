using FluxoCaixa.Consolidado.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;

namespace FluxoCaixa.Consolidado.Api.HealthChecks;

public sealed class MongoDbHealthCheck(MongoDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext healthCheckContext,
        CancellationToken cancellationToken = default)
    {
        await context.Client
            .GetDatabase("admin")
            .RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);

        return HealthCheckResult.Healthy();
    }
}
