import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from 'environments/environment';
import { SaldoConsolidado } from 'app/core/models/models';

@Injectable({ providedIn: 'root' })
export class ConsolidadoApiService {
  private readonly http = inject(HttpClient);
  private readonly base = `${environment.consolidadoApiUrl}/api/v1/consolidado`;

  obterPorData(data: string): Observable<SaldoConsolidado> {
    return this.http.get<SaldoConsolidado>(`${this.base}/${data}`);
  }

  obterPeriodo(inicio: string, fim: string): Observable<SaldoConsolidado[]> {
    return this.http.get<SaldoConsolidado[]>(`${this.base}/periodo`, {
      params: { inicio, fim }
    });
  }
}
