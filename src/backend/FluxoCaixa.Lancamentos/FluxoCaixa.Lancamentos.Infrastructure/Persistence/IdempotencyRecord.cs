namespace FluxoCaixa.Lancamentos.Infrastructure.Persistence;

public sealed class IdempotencyRecord
{
    public string Chave { get; set; } = string.Empty;
    public Guid EventoId { get; set; }
    public string HashRequisicao { get; set; } = string.Empty;
    public string Resposta { get; set; } = "{}";
    public DateTime CriadoEm { get; set; }
}
