import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';

/** `up`: answered 200. `down`: answered 503. `unreachable`: no answer, or anything else. */
export type ProbeState = 'up' | 'down' | 'unreachable';

export interface ApiHealth {
  /** /health/live: the API process answers. */
  readonly api: ProbeState;
  /** /health/ready: the API can reach its database. */
  readonly database: ProbeState;
}

/** Reads the API's health endpoints. Paths are relative; the dev server proxies them to the API. */
@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);

  async check(): Promise<ApiHealth> {
    const [api, database] = await Promise.all([
      this.probe('/health/live'),
      this.probe('/health/ready'),
    ]);
    return { api, database };
  }

  private async probe(path: string): Promise<ProbeState> {
    try {
      await firstValueFrom(this.http.get(path, { responseType: 'text' }));
      return 'up';
    } catch (error) {
      return error instanceof HttpErrorResponse && error.status === 503 ? 'down' : 'unreachable';
    }
  }
}
