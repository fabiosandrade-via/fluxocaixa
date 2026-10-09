namespace FluxoCaixa.SharedKernel.Events;

public sealed record DlqFailureEnvelope(
    int VersaoEnvelope,
    Guid FalhaId,
    Guid? EventoId,
    OriginalKafkaMessage MensagemOriginal,
    FailureDetails Processamento,
    ReplayDetails Reprocessamento);

public sealed record OriginalKafkaMessage(
    string Topico,
    int Particao,
    long Offset,
    string? Chave,
    string Valor,
    IReadOnlyDictionary<string, string> Headers);

public sealed record FailureDetails(
    string ConsumerGroup,
    int Tentativas,
    DateTime PrimeiraFalhaEm,
    DateTime UltimaFalhaEm,
    string TipoErro,
    string ErroResumido);

public sealed record ReplayDetails(int Quantidade, Guid? UltimoReprocessamentoId);
