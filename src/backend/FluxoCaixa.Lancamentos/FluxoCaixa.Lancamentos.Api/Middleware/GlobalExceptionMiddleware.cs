using System.Net;
using System.Text.Json;

namespace FluxoCaixa.Lancamentos.Api.Middleware;

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
            _logger.LogWarning(ex, "Argumento inválido: {Message}", ex.Message);
            await EscreverRespostaAsync(context, HttpStatusCode.BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro não tratado: {Message}", ex.Message);
            await EscreverRespostaAsync(context, HttpStatusCode.InternalServerError, "Erro interno do servidor.");
        }
    }

    private static Task EscreverRespostaAsync(HttpContext context, HttpStatusCode statusCode, string mensagem)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var body = JsonSerializer.Serialize(new { erro = mensagem, timestamp = DateTime.UtcNow });
        return context.Response.WriteAsync(body);
    }
}
