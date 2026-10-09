using FluxoCaixa.EventPublisher.Infrastructure;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddEventPublisherInfrastructure(builder.Configuration);
await builder.Build().RunAsync();
