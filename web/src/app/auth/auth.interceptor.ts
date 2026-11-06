import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AppConfig } from '../config/app-config';
import { AuthService } from './auth.service';
import { tokenExpiry } from './google-identity';

/**
 * Sends the Google ID token with requests to our API (never to other sites). An expired token is
 * dropped instead of sent, and a 401 from the API signs the person out.
 */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const api = inject(AppConfig).apiUrl('/api/');
  const token = auth.token();
  if (!token || !request.url.startsWith(api)) {
    return next(request);
  }

  if (tokenExpiry(token) * 1000 <= Date.now()) {
    auth.clear();
    return next(request);
  }

  return next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        auth.clear();
      }
      return throwError(() => error);
    }),
  );
};
