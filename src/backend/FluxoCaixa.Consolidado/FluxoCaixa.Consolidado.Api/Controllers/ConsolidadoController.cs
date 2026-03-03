using FluxoCaixa.Consolidado.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace FluxoCaixa.Consolidado.Api.Controllers;

[ApiController]
[Route("api/v1/consolidado")]
[Produces("application/json")]
public sealed class ConsolidadoController : ControllerBase
{
    private readonly IConsolidadoService _service;

    public ConsolidadoController(IConsolidadoService service) => _service = service;

    /// <summary>Retorna o saldo consolidado de uma data específica.</summary>
    [HttpGet("{data}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ObterPorData(
        [FromRoute] DateOnly data,
        CancellationToken ct)
    {
        var resultado = await _service.ObterPorDataAsync(data, ct);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    /// <summary>Retorna saldos consolidados de um período.</summary>
    [HttpGet("periodo")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ObterPeriodo(
        [FromQuery] DateOnly inicio,
        [FromQuery] DateOnly fim,
        CancellationToken ct)
    {
        if (fim < inicio)
            return BadRequest("Data fim deve ser maior ou igual à data início.");

        var resultado = await _service.ObterPeriodoAsync(inicio, fim, ct);
        return Ok(resultado);
    }
}
