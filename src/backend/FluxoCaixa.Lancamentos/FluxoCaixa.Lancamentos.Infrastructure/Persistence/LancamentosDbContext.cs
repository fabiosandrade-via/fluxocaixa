using FluxoCaixa.Lancamentos.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FluxoCaixa.Lancamentos.Infrastructure.Persistence;

public sealed class LancamentosDbContext : DbContext
{
    public LancamentosDbContext(DbContextOptions<LancamentosDbContext> options)
        : base(options) { }

    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<EventStoreRecord> Eventos => Set<EventStoreRecord>();
    public DbSet<IdempotencyRecord> ChavesIdempotencia => Set<IdempotencyRecord>();

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

        modelBuilder.Entity<EventStoreRecord>(entity =>
        {
            entity.ToTable("eventos");
            entity.HasKey(e => e.PosicaoGlobal);
            entity.Property(e => e.PosicaoGlobal).HasColumnName("posicao_global").ValueGeneratedNever();
            entity.Property(e => e.EventoId).HasColumnName("evento_id").IsRequired();
            entity.HasIndex(e => e.EventoId).IsUnique();
            entity.Property(e => e.StreamId).HasColumnName("stream_id").IsRequired();
            entity.Property(e => e.VersaoStream).HasColumnName("versao_stream").IsRequired();
            entity.HasIndex(e => new { e.StreamId, e.VersaoStream }).IsUnique();
            entity.Property(e => e.TipoEvento).HasColumnName("tipo_evento").IsRequired();
            entity.Property(e => e.VersaoSchema).HasColumnName("versao_schema").IsRequired();
            entity.Property(e => e.Dados).HasColumnName("dados").HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.Metadados).HasColumnName("metadados").HasColumnType("jsonb").IsRequired();
            entity.Property(e => e.OcorridoEm).HasColumnName("ocorrido_em").IsRequired();
            entity.Property(e => e.RegistradoEm).HasColumnName("registrado_em").IsRequired();
        });

        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("chaves_idempotencia");
            entity.HasKey(record => record.Chave);
            entity.Property(record => record.Chave).HasColumnName("chave");
            entity.Property(record => record.EventoId).HasColumnName("evento_id").IsRequired();
            entity.HasIndex(record => record.EventoId).IsUnique();
            entity.Property(record => record.HashRequisicao).HasColumnName("hash_requisicao").IsRequired();
            entity.Property(record => record.Resposta).HasColumnName("resposta").HasColumnType("jsonb").IsRequired();
            entity.Property(record => record.CriadoEm).HasColumnName("criado_em").IsRequired();
        });
    }
}