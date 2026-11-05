import { HttpBackend, HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** Settings that differ per environment, read from /config.json when the app starts. */
export interface RuntimeConfig {
  /** Where the API is, e.g. https://ca-financegoals-api-test....azurecontainerapps.io. Empty: same origin. */
  readonly apiBaseUrl: string;
}

/**
 * The build is the same for every environment (#20); the deploy writes config.json next to it.
 * Locally config.json has an empty apiBaseUrl, so requests stay relative and `ng serve` proxies them.
 */
@Injectable({ providedIn: 'root' })
export class AppConfig {
  // HttpBackend skips interceptors: the config is needed before anything else can run.
  private readonly http = new HttpClient(inject(HttpBackend));
  private config: RuntimeConfig = { apiBaseUrl: '' };

  /** The API base URL without a trailing slash; empty for the same origin. */
  get apiBaseUrl(): string {
    return this.config.apiBaseUrl;
  }

  /** Loads /config.json. A missing or invalid file leaves the defaults, so the app still starts. */
  async load(): Promise<void> {
    try {
      const loaded = await firstValueFrom(this.http.get<Partial<RuntimeConfig>>('/config.json'));
      const apiBaseUrl = typeof loaded?.apiBaseUrl === 'string' ? loaded.apiBaseUrl : '';
      this.config = { apiBaseUrl: apiBaseUrl.replace(/\/+$/, '') };
    } catch {
      this.config = { apiBaseUrl: '' };
    }
  }

  /** An absolute API URL for a path such as /health/live. */
  apiUrl(path: string): string {
    return `${this.apiBaseUrl}${path}`;
  }
}
