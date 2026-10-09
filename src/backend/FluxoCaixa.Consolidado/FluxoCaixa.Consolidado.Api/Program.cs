using FluxoCaixa.Consolidado.Application.Interfaces;
using FluxoCaixa.Consolidado.Application.Services;
using FluxoCaixa.Consolidado.Api.Middleware;
using FluxoCaixa.Consolidado.Infrastructure;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using FluxoCaixa.Consolidado.Api.Swagger;
using FluxoCaixa.Consolidado.Api.HealthChecks;
using Microsoft.Extensions.Options;
using Prometheus;
using Swashbuckle.AspNetCore.SwaggerGen;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services
    .AddApiVersioning(options =>
    {
        options.ReportApiVersions = true;
        options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddMvc()
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'VVV";
        options.SubstituteApiVersionInUrl = true;
    });
builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
builder.Services.AddSwaggerGen();

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddScoped<IConsolidadoService, ConsolidadoService>();

builder.Services.AddHealthChecks()
    .AddCheck<MongoDbHealthCheck>("mongodb")
    .AddRedis(
        builder.Configuration.GetConnectionString("Redis")!,
        name: "redis");

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

app.UseCors("frontend");

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseSwagger();
var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
app.UseSwaggerUI(options =>
{
    foreach (var description in apiVersionProvider.ApiVersionDescriptions)
    {
        options.SwaggerEndpoint(
            $"/swagger/{description.GroupName}/swagger.json",
            $"FluxoCaixa - Consolidado Diário API {description.GroupName}");
    }
});

app.UseHttpMetrics();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapMetrics("/metrics");

await app.RunAsync();