import { environment } from '../environments/environment';

export const appEnvironment = {
  production: environment.production,
  // Relative on purpose: in production the backend serves this app from its own
  // wwwroot. For `ng serve`, /api is forwarded by the dev server - set the
  // backend port in proxy.conf.json.
  apiUrl: '/api',
};
