import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../config/app-config';

/** `up`: answered 200. `down`: answered 503. `unreachable`: no answer, or anything else. */
export type ProbeState = 'up' | 'down' | 'unreachable';

export interface ApiHealth {
  /** /health/live: the API process answers. */
  readonly api: ProbeState;
  /** /health/ready: the API can reach its database. */
  readonly database: ProbeState;
}

/** Reads the API's health endpoints, at the API base URL from config.json (relative locally). */
@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  async check(): Promise<ApiHealth> {
    const [api, database] = await Promise.all([
      this.probe('/health/live'),
      this.probe('/health/ready'),
    ]);
    return { api, database };
  }

  private async probe(path: string): Promise<ProbeState> {
    try {
      await firstValueFrom(this.http.get(this.config.apiUrl(path), { responseType: 'text' }));
      return 'up';
    } catch (error) {
      return error instanceof HttpErrorResponse && error.status === 503 ? 'down' : 'unreachable';
    }
  }
}
