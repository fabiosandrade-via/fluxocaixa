import { Component, inject, signal, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { DatePipe, CurrencyPipe, NgClass } from '@angular/common';
import { ConsolidadoApiService } from '../services/consolidado-api.service';
import { SaldoConsolidado } from 'app/core/models/models';

@Component({
  selector: 'app-consolidado',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatCardModule, MatFormFieldModule, MatInputModule,
    MatButtonModule, MatTableModule,
    DatePipe, CurrencyPipe, NgClass
  ],
  template: `
    <mat-card style="margin-bottom:24px;">
      <mat-card-header><mat-card-title>Consultar Período</mat-card-title></mat-card-header>
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="consultar()" style="display:flex;gap:16px;align-items:center;padding-top:16px;">
          <mat-form-field>
            <mat-label>Data Início</mat-label>
            <input matInput type="date" formControlName="inicio" />
          </mat-form-field>
          <mat-form-field>
            <mat-label>Data Fim</mat-label>
            <input matInput type="date" formControlName="fim" />
          </mat-form-field>
          <button mat-raised-button color="primary" type="submit" [disabled]="form.invalid">
            Consultar
          </button>
        </form>
      </mat-card-content>
    </mat-card>

    @if (saldos().length > 0) {
      <!-- Totalizadores -->
      <div style="display:flex;gap:16px;margin-bottom:24px;flex-wrap:wrap;">
        <mat-card style="flex:1;min-width:180px;">
          <mat-card-content>
            <div style="font-size:12px;color:#666;">Total Créditos</div>
            <div style="font-size:24px;font-weight:700;color:#28a745;">
              {{ totalCreditos() | currency:'BRL' }}
            </div>
          </mat-card-content>
        </mat-card>
        <mat-card style="flex:1;min-width:180px;">
          <mat-card-content>
            <div style="font-size:12px;color:#666;">Total Débitos</div>
            <div style="font-size:24px;font-weight:700;color:#dc3545;">
              {{ totalDebitos() | currency:'BRL' }}
            </div>
          </mat-card-content>
        </mat-card>
        <mat-card style="flex:1;min-width:180px;">
          <mat-card-content>
            <div style="font-size:12px;color:#666;">Saldo Final</div>
            <div [ngClass]="saldoTotal() >= 0 ? 'positivo' : 'negativo'"
                 style="font-size:24px;font-weight:700;">
              {{ saldoTotal() | currency:'BRL' }}
            </div>
          </mat-card-content>
        </mat-card>
      </div>

      <!-- Tabela diária -->
      <mat-card>
        <mat-card-header><mat-card-title>Saldo Diário</mat-card-title></mat-card-header>
        <mat-card-content>
          <table mat-table [dataSource]="saldos()" style="width:100%;">
            <ng-container matColumnDef="data">
              <th mat-header-cell *matHeaderCellDef>Data</th>
              <td mat-cell *matCellDef="let s">{{ s.data }}</td>
            </ng-container>
            <ng-container matColumnDef="creditos">
              <th mat-header-cell *matHeaderCellDef>Créditos</th>
              <td mat-cell *matCellDef="let s" style="color:#28a745;">{{ s.totalCreditos | currency:'BRL' }}</td>
            </ng-container>
            <ng-container matColumnDef="debitos">
              <th mat-header-cell *matHeaderCellDef>Débitos</th>
              <td mat-cell *matCellDef="let s" style="color:#dc3545;">{{ s.totalDebitos | currency:'BRL' }}</td>
            </ng-container>
            <ng-container matColumnDef="saldo">
              <th mat-header-cell *matHeaderCellDef>Saldo</th>
              <td mat-cell *matCellDef="let s"
                  [ngClass]="s.saldoFinal >= 0 ? 'positivo' : 'negativo'">
                {{ s.saldoFinal | currency:'BRL' }}
              </td>
            </ng-container>
            <ng-container matColumnDef="atualizado">
              <th mat-header-cell *matHeaderCellDef>Atualizado em</th>
              <td mat-cell *matCellDef="let s">{{ s.atualizadoEm | date:'dd/MM HH:mm' }}</td>
            </ng-container>
            <tr mat-header-row *matHeaderRowDef="colunas"></tr>
            <tr mat-row *matRowDef="let row; columns: colunas;"></tr>
          </table>
        </mat-card-content>
      </mat-card>
    } @else {
      <mat-card>
        <mat-card-content style="text-align:center;padding:24px;">
          <strong>Não existem lançamentos consolidados para o período informado.</strong>
        </mat-card-content>
      </mat-card>
    }
  `,
  styles: [`
    .positivo { color: #28a745; font-weight: 600; }
    .negativo { color: #dc3545; font-weight: 600; }
  `]
})
export class ConsolidadoComponent implements OnInit {
  private readonly api = inject(ConsolidadoApiService);
  private readonly fb = inject(FormBuilder);

  readonly colunas = ['data', 'creditos', 'debitos', 'saldo', 'atualizado'];
  readonly saldos = signal<SaldoConsolidado[]>([]);

  readonly totalCreditos = () => this.saldos().reduce((acc, s) => acc + s.totalCreditos, 0);
  readonly totalDebitos = () => this.saldos().reduce((acc, s) => acc + s.totalDebitos, 0);
  readonly saldoTotal = () => this.totalCreditos() - this.totalDebitos();

  readonly form = this.fb.group({
    inicio: [this.primeiroDiaMes(), Validators.required],
    fim: [new Date().toISOString().substring(0, 10), Validators.required]
  });

  ngOnInit(): void {
    this.consultar();
  }

  consultar(): void {
    const { inicio, fim } = this.form.value;
    if (!inicio || !fim) return;
    this.api.obterPeriodo(inicio, fim).subscribe(data => this.saldos.set(data));
  }

  private primeiroDiaMes(): string {
    const d = new Date();
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-01`;
  }
}
