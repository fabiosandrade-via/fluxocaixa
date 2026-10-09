using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.Lancamentos.Application.UseCases.ListarLancamentos;
using FluxoCaixa.Lancamentos.Application.UseCases.RegistrarLancamento;
using FluxoCaixa.Lancamentos.Domain.Entities;
using FluxoCaixa.Lancamentos.Domain.Repositories;
using FluxoCaixa.SharedKernel.Events;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace FluxoCaixa.Lancamentos.Application.Services;

/// <summary>
/// Implementação do serviço de lançamentos.
/// </summary>
public sealed class LancamentoService : ILancamentoService
{
    private readonly ILancamentoRepository _repository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LancamentoService> _logger;

    public LancamentoService(
        ILancamentoRepository repository,
        IUnitOfWork unitOfWork,
        ILogger<LancamentoService> logger)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<RegistrarLancamentoResponse> RegistrarAsync(
        RegistrarLancamentoRequest request,
        CancellationToken ct = default)
        => await RegistrarAsync(request, idempotencyKey: null, ct: ct);

    public async Task<RegistrarLancamentoResponse> RegistrarAsync(
        RegistrarLancamentoRequest request,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        idempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey.Trim();
        if (idempotencyKey is { Length: > 128 })
            throw new ArgumentException("Idempotency-Key deve ter no máximo 128 caracteres.", nameof(idempotencyKey));

        var requestHash = CreateRequestHash(request);
        if (idempotencyKey is not null)
        {
            var savedResponse = await _unitOfWork.ObterRespostaIdempotenteAsync(
                idempotencyKey,
                requestHash,
                ct);
            if (savedResponse is not null)
                return savedResponse;
        }

        _logger.LogInformation(
            "Registrando lançamento: Tipo={Tipo}, Valor={Valor}, Data={Data}",
            request.Tipo, request.Valor, request.Data);

        var tipo = Enum.Parse<TipoLancamento>(request.Tipo);
        var lancamento = Lancamento.Criar(tipo, request.Valor, request.Data, request.Descricao);

        _repository.Adicionar(lancamento);

        var evento = new LancamentoRegistradoEvent
        {
            LancamentoId = lancamento.Id,
            Tipo = lancamento.Tipo.ToString(),
            Valor = lancamento.Valor.Valor,
            Data = lancamento.Data,
            Descricao = lancamento.Descricao
        };

        var response = new RegistrarLancamentoResponse
        {
            Id = lancamento.Id,
            Tipo = lancamento.Tipo.ToString(),
            Valor = lancamento.Valor.Valor,
            Data = lancamento.Data,
            Descricao = lancamento.Descricao,
            CriadoEm = lancamento.CriadoEm
        };

        var replayResponse = await _unitOfWork.CommitAsync(
            evento,
            $"lancamento:{lancamento.Id}",
            versaoStream: 1,
            idempotencyKey,
            idempotencyKey is null ? null : requestHash,
            idempotencyKey is null ? null : JsonSerializer.Serialize(response),
            ct);
        if (replayResponse is not null)
            return replayResponse;

        _logger.LogInformation("Lançamento {Id} registrado no Event Store.", lancamento.Id);

        return response;
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

    private static string CreateRequestHash(RegistrarLancamentoRequest request)
    {
        var canonical = string.Join(
            '|',
            request.Tipo.Trim(),
            request.Valor.ToString(System.Globalization.CultureInfo.InvariantCulture),
            request.Data.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            request.Descricao.Trim());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
