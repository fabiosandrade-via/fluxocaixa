import { Routes } from '@angular/router';

export const routes: Routes = [
  { path: '', redirectTo: 'lancamentos', pathMatch: 'full' },
  {
    path: 'lancamentos',
    loadComponent: () =>
      import('./features/lancamentos/components/lancamentos.component')
        .then(m => m.LancamentosComponent)
  },
  {
    path: 'consolidado',
    loadComponent: () =>
      import('./features/consolidado/components/consolidado.component')
        .then(m => m.ConsolidadoComponent)
  }
];
