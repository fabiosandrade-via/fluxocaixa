using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.Lancamentos.Domain.Repositories;
using FluxoCaixa.Lancamentos.Infrastructure.Messaging;
using FluxoCaixa.Lancamentos.Infrastructure.Persistence;
using FluxoCaixa.Lancamentos.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FluxoCaixa.Lancamentos.Infrastructure;

/// <summary>
/// Garante que a camada de apresentação não instancie serviços diretamente.
/// </summary>
public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<LancamentosDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("LancamentosDb"),
                npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 3)
            )
        );

        services.AddScoped<ILancamentoRepository, LancamentoRepository>();

        services.Configure<KafkaOptions>(configuration.GetSection("Kafka"));
        services.AddSingleton<IEventPublisher, KafkaEventPublisher>();

        return services;
    }

    public static async Task InicializarBancoDadosAsync(this IServiceProvider provider)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
