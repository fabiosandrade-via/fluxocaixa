import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from 'environments/environment';
import { Lancamento, RegistrarLancamentoRequest } from 'app/core/models/models';

@Injectable({ providedIn: 'root' })
export class LancamentosApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.lancamentosApiUrl}/api/v1/lancamentos`;

  registrar(request: RegistrarLancamentoRequest): Observable<Lancamento> {
    return this.http.post<Lancamento>(this.base, request);
  }

  listarTodos(): Observable<Lancamento[]> {
    return this.http.get<Lancamento[]>(this.base);
  }

  listarPorData(data: string): Observable<Lancamento[]> {
    return this.http.get<Lancamento[]>(`${this.base}/data/${data}`);
  }
}
