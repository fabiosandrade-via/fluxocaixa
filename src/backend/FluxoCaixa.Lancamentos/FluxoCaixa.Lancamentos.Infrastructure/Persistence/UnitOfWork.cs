using System.Text.Json;
using FluxoCaixa.Lancamentos.Application.Interfaces;
using FluxoCaixa.Lancamentos.Application.UseCases.RegistrarLancamento;
using FluxoCaixa.SharedKernel.Events;
using Microsoft.EntityFrameworkCore;

namespace FluxoCaixa.Lancamentos.Infrastructure.Persistence;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly LancamentosDbContext _context;

    public UnitOfWork(LancamentosDbContext context) => _context = context;

    public async Task<RegistrarLancamentoResponse?> CommitAsync(
        DomainEvent evento,
        string streamId,
        long versaoStream,
        string? idempotencyKey,
        string? requestHash,
        string? responseJson,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evento);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(versaoStream);
        if (idempotencyKey is not null && (requestHash is null || responseJson is null))
            throw new ArgumentException("Chave, hash e corpo idempotente devem ser fornecidos em conjunto.");
        if (idempotencyKey is null && (requestHash is not null || responseJson is not null))
            throw new ArgumentException("Hash e corpo idempotente exigem uma chave.");

        var storedEvent = new EventStoreRecord
        {
            EventoId = evento.EventId,
            StreamId = streamId,
            VersaoStream = versaoStream,
            TipoEvento = evento.EventType,
            VersaoSchema = 1,
            Dados = JsonSerializer.Serialize(evento, evento.GetType()),
            Metadados = "{}",
            OcorridoEm = evento.OcorridoEm,
            RegistradoEm = DateTime.UtcNow
        };
        _context.Eventos.Add(storedEvent);

        if (idempotencyKey is not null)
        {
            _context.ChavesIdempotencia.Add(new IdempotencyRecord
            {
                Chave = idempotencyKey,
                EventoId = evento.EventId,
                HashRequisicao = requestHash ?? throw new InvalidOperationException("Hash idempotente não informado."),
                Resposta = responseJson ?? throw new InvalidOperationException("Resposta idempotente não informada."),
                CriadoEm = DateTime.UtcNow
            });
        }

        var strategy = _context.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _context.Database.BeginTransactionAsync(ct);

                var positions = _context.Database
                    .SqlQueryRaw<long>(
                        "UPDATE public.controle_posicao_eventos " +
                        "SET ultima_posicao = ultima_posicao + 1 " +
                        "WHERE id = 1 RETURNING ultima_posicao AS \"Value\"")
                    .AsAsyncEnumerable();
                await using var positionEnumerator = positions.GetAsyncEnumerator(ct);
                if (!await positionEnumerator.MoveNextAsync())
                    throw new InvalidOperationException("Não foi possível obter a posição global do evento.");
                storedEvent.PosicaoGlobal = positionEnumerator.Current;
                if (await positionEnumerator.MoveNextAsync())
                    throw new InvalidOperationException("O controle de posição retornou mais de uma linha.");

                var savedChanges = await _context.SaveChangesAsync(
                    acceptAllChangesOnSuccess: false,
                    cancellationToken: ct);
                await transaction.CommitAsync(ct);

                return savedChanges;
            });
            _context.ChangeTracker.AcceptAllChanges();
            return null;
        }
        catch (DbUpdateException) when (idempotencyKey is not null && requestHash is not null)
        {
            var savedResponse = await ObterRespostaIdempotenteAsync(
                idempotencyKey,
                requestHash,
                CancellationToken.None);
            if (savedResponse is not null)
                return savedResponse;
            throw;
        }
    }

    public async Task<RegistrarLancamentoResponse?> ObterRespostaIdempotenteAsync(
        string idempotencyKey,
        string requestHash,
        CancellationToken ct = default)
    {
        var record = await _context.ChavesIdempotencia
            .AsNoTracking()
            .Where(record => record.Chave == idempotencyKey)
            .FirstOrDefaultAsync(ct);

        if (record is null)
            return null;
        if (!string.Equals(record.HashRequisicao, requestHash, StringComparison.Ordinal))
            throw new IdempotencyConflictException(
                "A Idempotency-Key já foi utilizada com um conteúdo de requisição diferente.");

        return JsonSerializer.Deserialize<RegistrarLancamentoResponse>(record.Resposta)
            ?? throw new InvalidOperationException("A resposta idempotente persistida não pôde ser desserializada.");
    }
}
