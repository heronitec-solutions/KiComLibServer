import { inject } from '@angular/core';
import { CanActivateChildFn, Router } from '@angular/router';
import { map } from 'rxjs';

import { AuthService } from './auth.service';

export const authGuard: CanActivateChildFn = (route, state) => {
  if (route.routeConfig?.path === 'login') {
    return true;
  }

  const auth = inject(AuthService);
  const router = inject(Router);

  return auth.ensureSession().pipe(
    map(profile => {
      if (profile.authenticated) {
        if (profile.mustChangePassword && route.routeConfig?.path !== 'account') {
          return router.createUrlTree(['/account']);
        }
        if (route.data?.['adminOnly'] === true && !profile.isAdmin) {
          return router.createUrlTree(['/home']);
        }
        if (route.data?.['warehouse'] === true && !profile.canReadWarehouse && !profile.canWriteWarehouse) {
          return router.createUrlTree(['/home']);
        }
        return true;
      }

      const returnUrl = state.url.startsWith('/login') ? '/home' : state.url;
      return router.createUrlTree(['/login'], {
        queryParams: { returnUrl }
      });
    })
  );
};
