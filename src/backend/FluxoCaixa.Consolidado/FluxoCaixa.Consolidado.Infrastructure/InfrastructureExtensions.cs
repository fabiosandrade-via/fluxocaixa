using FluxoCaixa.Consolidado.Domain.Repositories;
using FluxoCaixa.Consolidado.Infrastructure.Messaging;
using FluxoCaixa.Consolidado.Infrastructure.Persistence;
using FluxoCaixa.Consolidado.Infrastructure.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxoCaixa.Consolidado.Infrastructure;

/// <summary>
/// Registro de IoC para toda a infraestrutura do serviço Consolidado.
/// </summary>
public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<MongoDbOptions>(configuration.GetSection("MongoDB"));
        services.AddSingleton<MongoDbContext>(); 

        services.AddScoped<ISaldoConsolidadoRepository, SaldoConsolidadoRepository>();

        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
            options.InstanceName = "consolidado:";
        });

        services.Configure<KafkaConsumerOptions>(configuration.GetSection("Kafka"));
        services.AddHostedService<KafkaConsumerService>();

        return services;
    }
}
