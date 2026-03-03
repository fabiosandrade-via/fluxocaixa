using FluxoCaixa.Lancamentos.Domain.Entities;
using FluxoCaixa.Lancamentos.Domain.Repositories;
using FluxoCaixa.Lancamentos.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FluxoCaixa.Lancamentos.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implementação do repositório com EF Core e PostgreSQL.
/// </summary>
public sealed class LancamentoRepository : ILancamentoRepository
{
    private readonly LancamentosDbContext _context;

    public LancamentoRepository(LancamentosDbContext context) => _context = context;

    public async Task AdicionarAsync(Lancamento lancamento, CancellationToken ct = default)
    {
        await _context.Lancamentos.AddAsync(lancamento, ct);
        await _context.SaveChangesAsync(ct);
    }

    public Task<Lancamento?> ObterPorIdAsync(Guid id, CancellationToken ct = default)
        => _context.Lancamentos.AsNoTracking()
                   .FirstOrDefaultAsync(l => l.Id == id, ct);

    public async Task<IReadOnlyList<Lancamento>> ListarPorDataAsync(DateOnly data, CancellationToken ct = default)
    {
        var result = await _context.Lancamentos.AsNoTracking()
                                   .Where(l => l.Data == data)
                                   .OrderByDescending(l => l.CriadoEm)
                                   .ToListAsync(ct);
        return result.AsReadOnly();
    }

    public async Task<IReadOnlyList<Lancamento>> ListarTodosAsync(CancellationToken ct = default)
    {
        var result = await _context.Lancamentos.AsNoTracking()
                                   .OrderByDescending(l => l.CriadoEm)
                                   .ToListAsync(ct);
        return result.AsReadOnly();
    }
}
