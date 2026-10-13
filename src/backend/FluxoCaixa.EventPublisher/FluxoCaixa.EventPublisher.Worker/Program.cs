using Microsoft.AspNetCore.Builder;
using FluxoCaixa.EventPublisher.Infrastructure;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEventPublisherInfrastructure(builder.Configuration);
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("fluxocaixa-event-publisher"))
    .WithTracing(tracing => tracing
        .AddSource("FluxoCaixa.EventPublisher")
        .AddOtlpExporter());
var app = builder.Build();
app.MapMetrics();
await app.RunAsync();
