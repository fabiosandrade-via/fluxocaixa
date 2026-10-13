import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { environment } from 'environments/environment';
import { AuthService } from '../services/auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const isApiRequest =
    request.url.startsWith(environment.lancamentosApiUrl) ||
    request.url.startsWith(environment.consolidadoApiUrl);
  if (!isApiRequest) {
    return next(request);
  }

  return from(inject(AuthService).getAccessToken()).pipe(
    switchMap(token => next(request.clone({
      setHeaders: { Authorization: `Bearer ${token}` }
    })))
  );
};
