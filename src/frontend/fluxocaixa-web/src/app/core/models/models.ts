export interface Lancamento {
  id: string;
  tipo: 'Debito' | 'Credito';
  valor: number;
  data: string;
  descricao: string;
  criadoEm: string;
}

export interface RegistrarLancamentoRequest {
  tipo: 'Debito' | 'Credito';
  valor: number;
  data: string;
  descricao: string;
}

export interface SaldoConsolidado {
  id: string;
  data: string;
  totalCreditos: number;
  totalDebitos: number;
  saldoFinal: number;
  atualizadoEm: string;
}
