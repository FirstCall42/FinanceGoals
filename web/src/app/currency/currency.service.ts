import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../config/app-config';

/** A currency the app supports, as GET /api/currencies returns it. */
export interface CurrencyInfo {
  readonly code: string;
  readonly numericCode: number;
  readonly minorUnits: number;
  readonly name: string;
}

/** The supported currencies, loaded once from the API. */
@Injectable({ providedIn: 'root' })
export class CurrencyService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);
  private loading: Promise<void> | undefined;

  readonly currencies = signal<readonly CurrencyInfo[]>([]);
  readonly failed = signal(false);

  /** Loads the list once; later calls reuse it. A failure leaves an empty list and sets failed. */
  load(): Promise<void> {
    this.loading ??= firstValueFrom(
      this.http.get<CurrencyInfo[]>(this.config.apiUrl('/api/currencies')),
    ).then(
      (list) => this.currencies.set(list),
      () => {
        this.failed.set(true);
        this.loading = undefined;
      },
    );
    return this.loading;
  }
}
