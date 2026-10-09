using System.Net;
using System.Text.Json;

namespace FluxoCaixa.Consolidado.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Argumento inválido.");
            await EscreverRespostaAsync(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro não tratado.");
            await EscreverRespostaAsync(context, HttpStatusCode.InternalServerError, "Erro interno do servidor.");
        }
    }

    private static Task EscreverRespostaAsync(HttpContext ctx, HttpStatusCode code, string msg)
    {
        ctx.Response.ContentType = "application/json";
        ctx.Response.StatusCode = (int)code;
        return ctx.Response.WriteAsync(JsonSerializer.Serialize(new { erro = msg, timestamp = DateTime.UtcNow }));
    }
}
