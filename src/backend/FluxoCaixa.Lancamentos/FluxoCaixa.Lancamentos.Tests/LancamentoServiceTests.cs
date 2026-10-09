using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.Lancamentos.Application.Services;
using FluxoCaixa.Lancamentos.Application.UseCases.ListarLancamentos;
using FluxoCaixa.Lancamentos.Application.UseCases.RegistrarLancamento;
using FluxoCaixa.Lancamentos.Domain.Entities;
using FluxoCaixa.Lancamentos.Domain.Repositories;
using FluxoCaixa.SharedKernel.Events;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FluxoCaixa.Lancamentos.Tests;

public sealed class LancamentoServiceTests
{
    private static readonly RegistrarLancamentoRequest Request = new()
    {
        Tipo = "Credito",
        Valor = 25.50m,
        Data = new DateOnly(2026, 10, 7),
        Descricao = " Venda "
    };

    [Fact]
    public async Task RegistrarAsync_persiste_lancamento_e_commit_evento_com_chave_normalizada()
    {
        var repository = new FakeLancamentoRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(repository, unitOfWork);

        var response = await service.RegistrarAsync(Request, "  req-123  ");

        var saved = Assert.Single(repository.Added);
        var evento = Assert.IsType<LancamentoRegistradoEvent>(unitOfWork.Evento);
        Assert.Equal(saved.Id, response.Id);
        Assert.Equal(saved.Id, evento.LancamentoId);
        Assert.Equal("Credito", evento.Tipo);
        Assert.Equal(Request.Valor, evento.Valor);
        Assert.Equal(Request.Data, evento.Data);
        Assert.Equal("Venda", evento.Descricao);
        Assert.Equal($"lancamento:{saved.Id}", unitOfWork.StreamId);
        Assert.Equal(1, unitOfWork.VersaoStream);
        Assert.Equal("req-123", unitOfWork.IdempotencyKey);
        Assert.NotNull(unitOfWork.RequestHash);
        Assert.NotNull(unitOfWork.ResponseJson);
    }

    [Fact]
    public async Task RegistrarAsync_retorna_resposta_idempotente_sem_adicionar_outra_entidade()
    {
        var replay = new RegistrarLancamentoResponse
        {
            Id = Guid.NewGuid(),
            Tipo = "Credito",
            Valor = Request.Valor,
            Data = Request.Data,
            Descricao = Request.Descricao.Trim()
        };
        var repository = new FakeLancamentoRepository();
        var unitOfWork = new FakeUnitOfWork { RespostaIdempotente = replay };
        var service = CreateService(repository, unitOfWork);

        var response = await service.RegistrarAsync(Request, "req-123");

        Assert.Same(replay, response);
        Assert.Empty(repository.Added);
        Assert.Null(unitOfWork.Evento);
    }

    [Fact]
    public async Task RegistrarAsync_rejeita_chave_maior_que_128_caracteres_antes_de_persistir()
    {
        var repository = new FakeLancamentoRepository();
        var unitOfWork = new FakeUnitOfWork();
        var service = CreateService(repository, unitOfWork);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.RegistrarAsync(Request, new string('x', 129)));

        Assert.Empty(repository.Added);
        Assert.Null(unitOfWork.Evento);
    }

    private static LancamentoService CreateService(
        ILancamentoRepository repository,
        IUnitOfWork unitOfWork)
        => new(repository, unitOfWork, NullLogger<LancamentoService>.Instance);

    private sealed class FakeLancamentoRepository : ILancamentoRepository
    {
        public List<Lancamento> Added { get; } = [];

        public void Adicionar(Lancamento lancamento) => Added.Add(lancamento);
        public Task<Lancamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<Lancamento?>(null);
        public Task<IReadOnlyList<Lancamento>> ListarPorDataAsync(DateOnly data, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Lancamento>>([]);
        public Task<IReadOnlyList<Lancamento>> ListarTodosAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Lancamento>>([]);
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public RegistrarLancamentoResponse? RespostaIdempotente { get; init; }
        public DomainEvent? Evento { get; private set; }
        public string? StreamId { get; private set; }
        public long VersaoStream { get; private set; }
        public string? IdempotencyKey { get; private set; }
        public string? RequestHash { get; private set; }
        public string? ResponseJson { get; private set; }

        public Task<RegistrarLancamentoResponse?> ObterRespostaIdempotenteAsync(
            string idempotencyKey,
            string requestHash,
            CancellationToken ct = default)
            => Task.FromResult(RespostaIdempotente);

        public Task<RegistrarLancamentoResponse?> CommitAsync(
            DomainEvent evento,
            string streamId,
            long versaoStream,
            string? idempotencyKey,
            string? requestHash,
            string? responseJson,
            CancellationToken ct = default)
        {
            Evento = evento;
            StreamId = streamId;
            VersaoStream = versaoStream;
            IdempotencyKey = idempotencyKey;
            RequestHash = requestHash;
            ResponseJson = responseJson;
            return Task.FromResult<RegistrarLancamentoResponse?>(null);
        }
    }
}
