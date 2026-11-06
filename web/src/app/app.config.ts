import { provideHttpClient, withFetch, withInterceptors } from '@angular/common/http';
import {
  ApplicationConfig,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { routes } from './app.routes';
import { authInterceptor } from './auth/auth.interceptor';
import { ApiConfiguration } from './api/api-configuration';
import { AppConfig } from './config/app-config';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes, withComponentInputBinding()),
    // The interceptor adds the sign-in token to requests to our API.
    provideHttpClient(withFetch(), withInterceptors([authInterceptor])),
    // Read config.json (the API base URL) before the app starts, and point the generated API
    // client (src/app/api) at it.
    provideAppInitializer(async () => {
      // inject() only works before the first await.
      const config = inject(AppConfig);
      const api = inject(ApiConfiguration);
      await config.load();
      api.rootUrl = config.apiBaseUrl;
    }),
  ],
};
