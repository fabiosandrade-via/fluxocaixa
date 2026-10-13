using Microsoft.AspNetCore.Builder;
using FluxoCaixa.Projetor.Infrastructure;
using FluxoCaixa.Projetor.Infrastructure.Persistence;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<ProjectorMongoOptions>(
    builder.Configuration.GetSection("MongoDB"));
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
    options.InstanceName = "consolidado:";
});
builder.Services.AddSingleton<ProjectorMongoContext>();
builder.Services.AddScoped<IProjectionWriter, MongoProjectionWriter>();
builder.Services.AddProjectorInfrastructure(builder.Configuration);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("fluxocaixa-projetor"))
    .WithTracing(tracing => tracing
        .AddSource("FluxoCaixa.Projetor")
        .AddOtlpExporter());
var app = builder.Build();
app.MapMetrics();
await app.RunAsync();
