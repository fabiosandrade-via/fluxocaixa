using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.Lancamentos.Application.UseCases.RegistrarLancamento;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;

namespace FluxoCaixa.Lancamentos.Api.Controllers;

[ApiController]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/lancamentos")]
[Produces("application/json")]
public sealed class LancamentosController : ControllerBase
{
    private readonly ILancamentoService _service;
    private readonly ILogger<LancamentosController> _logger;

    public LancamentosController(
        ILancamentoService service,
        ILogger<LancamentosController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Registra um novo lançamento (débito ou crédito).</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Registrar(
        [FromBody] RegistrarLancamentoRequest request,
        CancellationToken ct,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey = null)
    {
        if (idempotencyKey?.Length > 128)
            return BadRequest("Idempotency-Key deve ter no máximo 128 caracteres.");

        RegistrarLancamentoResponse response;
        try
        {
            response = await _service.RegistrarAsync(request, idempotencyKey, ct);
        }
        catch (IdempotencyConflictException ex)
        {
            return Conflict(ex.Message);
        }
        return CreatedAtAction(nameof(ObterPorData), new { data = response.Data }, response);
    }

    /// <summary>Lista todos os lançamentos.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListarTodos(CancellationToken ct)
    {
        var result = await _service.ListarTodosAsync(ct);
        return Ok(result);
    }

    /// <summary>Lista lançamentos de uma data específica.</summary>
    [HttpGet("data/{data:required}")]
    public async Task<IActionResult> ObterPorData(
        [FromRoute] string data,
        CancellationToken ct)
    {
        if (!DateOnly.TryParseExact(
                data,
                "dd/MM/yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out var dataConvertida))
        {
            return BadRequest("Data inválida. Use o formato dd/MM/yyyy.");
        }

        var result = await _service.ListarPorDataAsync(dataConvertida, ct);
        return Ok(result);
    }
}
