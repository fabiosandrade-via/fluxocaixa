using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.Lancamentos.Application.UseCases.ListarLancamentos;
using FluxoCaixa.Lancamentos.Application.UseCases.RegistrarLancamento;
using FluxoCaixa.Lancamentos.Domain.Entities;
using FluxoCaixa.Lancamentos.Domain.Repositories;
using FluxoCaixa.SharedKernel.Events;
using Microsoft.Extensions.Logging;

namespace FluxoCaixa.Lancamentos.Application.Services;

/// <summary>
/// Implementação do serviço de lançamentos.
/// </summary>
public sealed class LancamentoService : ILancamentoService
{
    private readonly ILancamentoRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<LancamentoService> _logger;

    public LancamentoService(
        ILancamentoRepository repository,
        IEventPublisher eventPublisher,
        ILogger<LancamentoService> logger)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<RegistrarLancamentoResponse> RegistrarAsync(
        RegistrarLancamentoRequest request,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Registrando lançamento: Tipo={Tipo}, Valor={Valor}, Data={Data}",
            request.Tipo, request.Valor, request.Data);

        var tipo = Enum.Parse<TipoLancamento>(request.Tipo);
        var lancamento = Lancamento.Criar(tipo, request.Valor, request.Data, request.Descricao);

        await _repository.AdicionarAsync(lancamento, ct);

        var evento = new LancamentoRegistradoEvent
        {
            LancamentoId = lancamento.Id,
            Tipo = lancamento.Tipo.ToString(),
            Valor = lancamento.Valor.Valor,
            Data = lancamento.Data,
            Descricao = lancamento.Descricao
        };

        await _eventPublisher.PublicarAsync(evento, ct);

        _logger.LogInformation("Lançamento {Id} registrado e evento publicado.", lancamento.Id);

        return new RegistrarLancamentoResponse
        {
            Id = lancamento.Id,
            Tipo = lancamento.Tipo.ToString(),
            Valor = lancamento.Valor.Valor,
            Data = lancamento.Data,
            Descricao = lancamento.Descricao,
            CriadoEm = lancamento.CriadoEm
        };
    }

    public async Task<IReadOnlyList<LancamentoDto>> ListarTodosAsync(CancellationToken ct = default)
    {
        var lancamentos = await _repository.ListarTodosAsync(ct);
        return lancamentos.Select(MapToDto).ToList().AsReadOnly();
    }

    public async Task<IReadOnlyList<LancamentoDto>> ListarPorDataAsync(
        DateOnly data,
        CancellationToken ct = default)
    {
        var lancamentos = await _repository.ListarPorDataAsync(data, ct);
        return lancamentos.Select(MapToDto).ToList().AsReadOnly();
    }

    private static LancamentoDto MapToDto(Lancamento l) => new()
    {
        Id = l.Id,
        Tipo = l.Tipo.ToString(),
        Valor = l.Valor.Valor,
        Data = l.Data,
        Descricao = l.Descricao,
        CriadoEm = l.CriadoEm
    };
}
