namespace FluxoCaixa.Lancamentos.Infrastructure.Persistence;

public sealed class EventStoreRecord
{
    public long PosicaoGlobal { get; set; }
    public Guid EventoId { get; set; }
    public string StreamId { get; set; } = string.Empty;
    public long VersaoStream { get; set; }
    public string TipoEvento { get; set; } = string.Empty;
    public int VersaoSchema { get; set; }
    public string Dados { get; set; } = "{}";
    public string Metadados { get; set; } = "{}";
    public DateTime OcorridoEm { get; set; }
    public DateTime RegistradoEm { get; set; }
}
