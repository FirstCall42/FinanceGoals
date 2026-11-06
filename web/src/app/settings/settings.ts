import { Component, computed, inject } from '@angular/core';
import { CurrencyService } from '../currency/currency.service';
import { DisplayCurrencyService } from '../currency/display-currency.service';
import { HouseholdSettings } from '../profile/household-settings';

/** Household settings. For now: the main currency (kept in this browser until profiles exist, #25). */
@Component({
  selector: 'app-settings',
  imports: [HouseholdSettings],
  template: `
    <section aria-labelledby="settings-title">
      <h1 id="settings-title">Settings</h1>
      <label class="field">
        <span>Main currency</span>
        <select [value]="display.main()" (change)="pick($event)">
          @for (c of currencies(); track c.code) {
            <option [value]="c.code" [selected]="c.code === display.main()">
              {{ c.code }}, {{ c.name }}
            </option>
          }
        </select>
        <small>Totals and charts start in this currency. Saved in this browser for now.</small>
      </label>
      @if (failed()) {
        <p role="status">The currency list couldn't be loaded. Try again later.</p>
      }
    </section>
    <app-household-settings />
  `,
  styles: `
    :host {
      display: flex;
      flex-direction: column;
      gap: 20px;
    }
    section {
      background: var(--fg-surface);
      border: 1px solid var(--fg-line);
      border-radius: var(--fg-radius);
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 16px;
      max-width: 560px;
    }
    h1 {
      margin: 0;
      font-size: 20px;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
    select {
      min-height: 44px;
      padding: 0 10px;
      border-radius: 10px;
      border: 1px solid var(--fg-line);
      background: var(--fg-surface);
      color: var(--fg-ink);
      font: inherit;
    }
    small {
      color: var(--fg-muted);
    }
  `,
})
export class Settings {
  protected readonly display = inject(DisplayCurrencyService);
  private readonly currencyService = inject(CurrencyService);

  protected readonly currencies = computed(() => {
    const list = this.currencyService.currencies();
    return list.length > 0 ? list : [{ code: this.display.main(), name: '' }];
  });
  protected readonly failed = this.currencyService.failed;

  constructor() {
    void this.currencyService.load();
  }

  protected pick(event: Event): void {
    this.display.setMain((event.target as HTMLSelectElement).value);
  }
}
