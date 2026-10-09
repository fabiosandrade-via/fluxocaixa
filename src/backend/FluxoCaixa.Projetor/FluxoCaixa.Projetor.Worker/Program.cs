using FluxoCaixa.Projetor.Infrastructure;
using FluxoCaixa.Projetor.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
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
await builder.Build().RunAsync();
