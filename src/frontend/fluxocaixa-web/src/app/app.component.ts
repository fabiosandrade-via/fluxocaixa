import { Component } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { CommonModule } from '@angular/common';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CommonModule],
  template: `
    <nav style="background:#3f51b5;padding:0 24px;display:flex;align-items:center;height:64px;color:white;">
      <span style="font-size:20px;font-weight:500;margin-right:32px;">💰 FluxoCaixa</span>
      <a routerLink="/lancamentos" routerLinkActive="nav-active"
         style="color:white;text-decoration:none;padding:8px 16px;border-radius:4px;margin-right:8px;">
        Lançamentos
      </a>
      <a routerLink="/consolidado" routerLinkActive="nav-active"
         style="color:white;text-decoration:none;padding:8px 16px;border-radius:4px;">
        Consolidado
      </a>
    </nav>
    <main style="padding:24px;max-width:1200px;margin:0 auto;">
      <router-outlet />
    </main>
  `,
  styles: [`
    .nav-active { background: rgba(255,255,255,0.2) !important; }
  `]
})
export class AppComponent {}
