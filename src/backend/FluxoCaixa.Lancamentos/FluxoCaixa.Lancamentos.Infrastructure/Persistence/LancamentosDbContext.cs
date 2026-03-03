using FluxoCaixa.Lancamentos.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluxoCaixa.Lancamentos.Infrastructure.Persistence;

public sealed class LancamentosDbContext : DbContext
{
    public LancamentosDbContext(DbContextOptions<LancamentosDbContext> options)
        : base(options) { }

    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Lancamento>(entity =>
        {
            entity.ToTable("lancamentos");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                  .HasColumnName("id")
                  .ValueGeneratedNever(); 

            entity.Property(e => e.Tipo)
                  .HasColumnName("tipo")
                  .HasConversion<string>()
                  .HasMaxLength(10)
                  .IsRequired();

            entity.OwnsOne(e => e.Valor, v =>
            {
                v.Property(d => d.Valor)
                 .HasColumnName("valor")
                 .HasColumnType("numeric(18,2)")
                 .IsRequired();
            });

            entity.Property(e => e.Data)
                  .HasColumnName("data")
                  .IsRequired();

            entity.Property(e => e.Descricao)
                  .HasColumnName("descricao")
                  .HasMaxLength(250)
                  .IsRequired();

            entity.Property(e => e.CriadoEm)
                  .HasColumnName("criado_em")
                  .IsRequired();
        });
    }
}