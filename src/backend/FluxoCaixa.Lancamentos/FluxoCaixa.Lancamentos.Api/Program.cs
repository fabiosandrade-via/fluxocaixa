using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.Lancamentos.Application.Services;
using FluxoCaixa.Lancamentos.Api.Middleware;
using FluxoCaixa.Lancamentos.Infrastructure;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "FluxoCaixa - Lançamentos API", Version = "v1" });
});

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
app.UseSwaggerUI();

app.UseHttpMetrics();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapMetrics("/metrics");

await app.RunAsync();
