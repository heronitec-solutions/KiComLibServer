import { Injectable, Injector } from '@angular/core';
import { HttpErrorResponse, HttpEvent, HttpHandler, HttpInterceptor, HttpRequest } from '@angular/common/http';
import { Router } from '@angular/router';
import { Observable, catchError, throwError } from 'rxjs';

import { AuthService } from './auth.service';

@Injectable()
export class AuthInterceptor implements HttpInterceptor {
  constructor(private injector: Injector) { }

  intercept(req: HttpRequest<unknown>, next: HttpHandler): Observable<HttpEvent<unknown>> {
    const isLogin = req.url.includes('/api/auth/login');
    const isLogout = req.url.includes('/api/auth/logout');
    const isSessionProbe = req.url.includes('/api/auth/me');
    const isApi = req.url.includes('/api/');
    const outgoing = isApi
      ? req.clone({ setHeaders: { 'X-KiComLib-Request': '1' } })
      : req;

    return next.handle(outgoing).pipe(
      catchError(error => {
        if (isApi && !isLogin && !isSessionProbe && !isLogout && error instanceof HttpErrorResponse && error.status === 401) {
          this.redirectToLogin();
        }
        return throwError(() => error);
      })
    );
  }

  private redirectToLogin(): void {
    const auth = this.injector.get(AuthService);
    const router = this.injector.get(Router);
    const returnUrl = router.url.startsWith('/login') ? '/home' : router.url;
    auth.clearSession();
    if (router.url.startsWith('/login')) {
      return;
    }

    void router.navigate(['/login'], {
      queryParams: { returnUrl, expired: '1' }
    });
  }
}
