import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

const mainKey = 'financegoals.mainCurrency';
const codePattern = /^[A-Z]{3}$/;

/**
 * Which currency amounts are shown in.
 * - The main currency: the household's own (NOK by default). Until household profiles exist (#25)
 *   it is kept in this browser; a follow-up moves it to the profile.
 * - The display currency: what totals and charts show right now; it starts as the main currency and
 *   the switcher changes it for this visit only.
 */
@Injectable({ providedIn: 'root' })
export class DisplayCurrencyService {
  private readonly storage = inject(DOCUMENT).defaultView?.localStorage;

  readonly main = signal(this.readMain());
  readonly current = signal(this.main());

  setMain(code: string): void {
    if (!codePattern.test(code)) {
      return;
    }

    this.main.set(code);
    this.current.set(code);
    try {
      this.storage?.setItem(mainKey, code);
    } catch {
      // Not remembered; it still applies for this visit.
    }
  }

  show(code: string): void {
    if (codePattern.test(code)) {
      this.current.set(code);
    }
  }

  private readMain(): string {
    try {
      const stored = this.storage?.getItem(mainKey);
      return stored && codePattern.test(stored) ? stored : 'NOK';
    } catch {
      return 'NOK';
    }
  }
}
