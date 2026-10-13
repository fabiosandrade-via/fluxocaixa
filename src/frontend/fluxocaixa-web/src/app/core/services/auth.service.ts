import { Injectable, signal } from '@angular/core';
import type Keycloak from 'keycloak-js';
import { environment } from 'environments/environment';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private keycloak?: Keycloak;

  readonly authenticated = signal(false);
  readonly username = signal('');

  async initialize(): Promise<void> {
    const KeycloakClient = (await import('keycloak-js')).default;
    this.keycloak = new KeycloakClient({
      url: environment.oidcUrl,
      realm: environment.oidcRealm,
      clientId: environment.oidcClientId
    });
    const authenticated = await this.keycloak.init({
      onLoad: 'login-required',
      pkceMethod: 'S256',
      checkLoginIframe: false
    });
    this.authenticated.set(authenticated);
    this.username.set(
      this.keycloak.tokenParsed?.['preferred_username']
        ?? this.keycloak.idTokenParsed?.['preferred_username']
        ?? 'Autenticado'
    );
  }

  async getAccessToken(): Promise<string> {
    const keycloak = this.keycloak;
    if (!keycloak?.authenticated) {
      throw new Error('A sessão OIDC não está autenticada.');
    }

    await keycloak.updateToken(30);
    if (!keycloak.token) {
      throw new Error('O provedor OIDC não retornou um access token.');
    }
    return keycloak.token;
  }

  async logout(): Promise<void> {
    if (!this.keycloak) {
      throw new Error('O provedor OIDC ainda não foi inicializado.');
    }
    await this.keycloak.logout({ redirectUri: window.location.origin });
  }
}
