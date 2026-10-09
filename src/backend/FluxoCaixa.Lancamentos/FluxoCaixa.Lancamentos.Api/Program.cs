using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.Lancamentos.Application.Services;
using FluxoCaixa.Lancamentos.Api.Middleware;
using FluxoCaixa.Lancamentos.Infrastructure;
using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using FluxoCaixa.Lancamentos.Api.Swagger;
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
builder.Services.AddScoped<ILancamentoService, LancamentoService>();

builder.Services.AddCors(opt => opt.AddPolicy("frontend", policy =>
    policy.AllowAnyOrigin()
          .AllowAnyHeader()
          .AllowAnyMethod()));

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("LancamentosDb")!);

var app = builder.Build();

await app.Services.InicializarBancoDadosAsync();

app.UseCors("frontend");

app.UseMiddleware<GlobalExceptionMiddleware>();

// Swagger sempre ativo para facilitar testes
app.UseSwagger();
var apiVersionProvider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
app.UseSwaggerUI(options =>
{
    foreach (var description in apiVersionProvider.ApiVersionDescriptions)
    {
        options.SwaggerEndpoint(
            $"/swagger/{description.GroupName}/swagger.json",
            $"FluxoCaixa - Lançamentos API {description.GroupName}");
    }
});

app.UseHttpMetrics();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapMetrics("/metrics");

await app.RunAsync();
