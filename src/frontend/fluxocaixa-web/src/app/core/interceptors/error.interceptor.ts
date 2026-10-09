import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  return next(req).pipe(
    catchError(err => {
      const message = err.error?.erro ?? err.message ?? 'Erro desconhecido';
      console.error(`[HTTP Error] ${err.status}: ${message}`);
      return throwError(() => new Error(message));
    })
  );
};
