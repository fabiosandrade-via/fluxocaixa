import { Component, inject, signal, computed, OnInit } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { MatButtonModule } from '@angular/material/button';
import { MatTableModule } from '@angular/material/table';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { DatePipe, CurrencyPipe, NgClass } from '@angular/common';
import { LancamentosApiService } from '../services/lancamentos-api.service';
import { Lancamento } from 'app/core/models/models';

@Component({
  selector: 'app-lancamentos',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    MatCardModule, MatFormFieldModule, MatInputModule,
    MatSelectModule, MatButtonModule, MatTableModule,
    MatDatepickerModule, MatNativeDateModule, MatSnackBarModule,
    DatePipe, CurrencyPipe, NgClass
  ],
  template: `
    <mat-card style="margin-bottom: 24px;">
      <mat-card-header>
        <mat-card-title>Registrar Lançamento</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <form [formGroup]="form" (ngSubmit)="registrar()" style="display:flex;gap:16px;flex-wrap:wrap;padding-top:16px;">
          <mat-form-field>
            <mat-label>Tipo</mat-label>
            <mat-select formControlName="tipo">
              <mat-option value="Credito">Crédito</mat-option>
              <mat-option value="Debito">Débito</mat-option>
            </mat-select>
          </mat-form-field>

          <mat-form-field>
            <mat-label>Valor (R$)</mat-label>
            <input matInput type="number" min="0.01" step="0.01" formControlName="valor" />
          </mat-form-field>

          <mat-form-field>
            <mat-label>Data</mat-label>
            <input matInput formControlName="data" placeholder="dd/MM/yyyy" />
          </mat-form-field>

          <mat-form-field style="flex:1;min-width:200px;">
            <mat-label>Descrição</mat-label>
            <input matInput formControlName="descricao" maxlength="250" />
          </mat-form-field>

          <div style="display:flex;align-items:center;">
            <button mat-raised-button color="primary" type="submit" [disabled]="form.invalid || carregando()">
              {{ carregando() ? 'Salvando...' : 'Registrar' }}
            </button>
          </div>
        </form>
      </mat-card-content>
    </mat-card>

    <mat-card>
      <mat-card-header>
        <mat-card-title>Lançamentos ({{ lancamentos().length }})</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        <table mat-table [dataSource]="lancamentos()" style="width:100%;">
          <ng-container matColumnDef="tipo">
            <th mat-header-cell *matHeaderCellDef>Tipo</th>
            <td mat-cell *matCellDef="let l">
              <span [ngClass]="l.tipo === 'Credito' ? 'chip-credito' : 'chip-debito'">{{ l.tipo }}</span>
            </td>
          </ng-container>
          <ng-container matColumnDef="valor">
            <th mat-header-cell *matHeaderCellDef>Valor</th>
            <td mat-cell *matCellDef="let l">{{ l.valor | currency:'BRL' }}</td>
          </ng-container>
          <ng-container matColumnDef="data">
            <th mat-header-cell *matHeaderCellDef>Data</th>
            <td mat-cell *matCellDef="let l">{{ l.data }}</td>
          </ng-container>
          <ng-container matColumnDef="descricao">
            <th mat-header-cell *matHeaderCellDef>Descrição</th>
            <td mat-cell *matCellDef="let l">{{ l.descricao }}</td>
          </ng-container>
          <ng-container matColumnDef="criadoEm">
            <th mat-header-cell *matHeaderCellDef>Criado em</th>
            <td mat-cell *matCellDef="let l">{{ l.criadoEm | date:'dd/MM/yyyy HH:mm' }}</td>
          </ng-container>

          <tr mat-header-row *matHeaderRowDef="colunas"></tr>
          <tr mat-row *matRowDef="let row; columns: colunas;"></tr>
        </table>
      </mat-card-content>
    </mat-card>
  `,
  styles: [`
    .chip-credito { background:#d4edda;color:#155724;padding:2px 8px;border-radius:12px;font-size:12px; }
    .chip-debito  { background:#f8d7da;color:#721c24;padding:2px 8px;border-radius:12px;font-size:12px; }
    mat-form-field { min-width: 160px; }
  `]
})
export class LancamentosComponent implements OnInit {
  private readonly api = inject(LancamentosApiService);
  private readonly fb = inject(FormBuilder);
  private readonly snack = inject(MatSnackBar);

  readonly colunas = ['tipo', 'valor', 'data', 'descricao', 'criadoEm'];
  readonly lancamentos = signal<Lancamento[]>([]);
  readonly carregando = signal(false);

  readonly form = this.fb.group({
    tipo: ['Credito', Validators.required],
    valor: [null as number | null, [Validators.required, Validators.min(0.01)]],
    data: [this.hojeBR(), Validators.required],
    descricao: ['', [Validators.required, Validators.maxLength(250)]]
  });

  ngOnInit(): void {
    this.api.listarTodos().subscribe(data => {
      const convertidos = data.map(l => ({
        ...l,
        data: this.converterParaBR(l.data)
      }));
      this.lancamentos.set(convertidos);
    });
  }

private hojeBR(): string {
  const d = new Date();
  return `${String(d.getDate()).padStart(2, '0')}/${
    String(d.getMonth() + 1).padStart(2, '0')}/${
    d.getFullYear()}`;
}

private converterParaIso(dataBR: string): string {
  const [dia, mes, ano] = dataBR.split('/');
  return `${ano}-${mes}-${dia}`;
}

private converterParaBR(dataIso: string): string {
  const [ano, mes, dia] = dataIso.split('-');
  return `${dia}/${mes}/${ano}`;
}

registrar(): void {
  if (this.form.invalid) return;
  this.carregando.set(true);

  const formValue = this.form.value as any;

  const req = {
    ...formValue,
    data: this.converterParaIso(formValue.data) // 🔥 conversão aqui
  };

  this.api.registrar(req).subscribe({
    next: (novo) => {
      // converte data recebida da API para BR
      novo.data = this.converterParaBR(novo.data);

      this.lancamentos.update(lista => [novo, ...lista]);
      this.form.reset({ tipo: 'Credito', data: this.hojeBR() });

      this.snack.open('Lançamento registrado com sucesso!', 'OK', { duration: 3000 });
      this.carregando.set(false);
    },
    error: (err) => {
      this.snack.open(`Erro: ${err.message}`, 'OK', { duration: 5000 });
      this.carregando.set(false);
    }
  });
}
}
