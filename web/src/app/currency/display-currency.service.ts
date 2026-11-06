import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import { AuthService } from '../auth/auth.service';

const codePattern = /^[A-Z]{3}$/;

/** Before sign-in, or without a household, amounts are shown in NOK. */
const defaultMain = 'NOK';

/**
 * Which currency amounts are shown in.
 * - The main currency: the household's own, stored on its profile (#105) so every member and
 *   device sees the same one. NOK until someone signs in.
 * - The display currency: what totals and charts show right now; it starts as the main currency and
 *   the switcher changes it for this visit only.
 */
@Injectable({ providedIn: 'root' })
export class DisplayCurrencyService {
  private readonly auth = inject(AuthService);
  private readonly chosen = signal<string | undefined>(undefined);

  readonly main = computed(() => this.auth.profiles()[0]?.mainCurrency ?? defaultMain);
  readonly current = computed(() => this.chosen() ?? this.main());

  constructor() {
    // The main currency used to live in this browser (#33); it is the household's setting now.
    try {
      inject(DOCUMENT).defaultView?.localStorage.removeItem('financegoals.mainCurrency');
    } catch {
      // Storage can be blocked; there is nothing to remove then.
    }
  }

  /** Shows amounts in another currency for this visit only. */
  show(code: string): void {
    if (codePattern.test(code)) {
      this.chosen.set(code);
    }
  }

  /** Back to the main currency, e.g. after the household's main currency changed. */
  reset(): void {
    this.chosen.set(undefined);
  }
}
