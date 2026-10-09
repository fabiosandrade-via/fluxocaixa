using FluxoCaixa.Consolidado.Infrastructure.Persistence;
using FluxoCaixa.DlqReprocessor.Infrastructure;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.Configure<MongoDbOptions>(builder.Configuration.GetSection("MongoDB"));
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddDlqReprocessorInfrastructure(builder.Configuration);
await builder.Build().RunAsync();
