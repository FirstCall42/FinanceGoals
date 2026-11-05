import { Component, computed, inject } from '@angular/core';
import { CurrencyService } from './currency.service';
import { DisplayCurrencyService } from './display-currency.service';

/**
 * Picks the currency totals and charts are shown in. The main currency comes first, then the
 * others by code. Until the list has loaded, only the current currency is offered.
 */
@Component({
  selector: 'app-currency-switcher',
  template: `
    <label class="switcher">
      <span>Show amounts in</span>
      <select [value]="display.current()" (change)="pick($event)">
        @for (option of options(); track option.code) {
          <option [value]="option.code" [selected]="option.code === display.current()">
            {{ option.label }}
          </option>
        }
      </select>
    </label>
  `,
  styles: `
    .switcher {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 13px;
      color: var(--fg-muted);
    }
    select {
      min-height: 44px;
      padding: 0 10px;
      border-radius: 10px;
      border: 1px solid var(--fg-line);
      background: var(--fg-surface);
      color: var(--fg-ink);
      font: inherit;
      font-size: 14px;
    }
  `,
})
export class CurrencySwitcher {
  protected readonly display = inject(DisplayCurrencyService);
  private readonly currencies = inject(CurrencyService);

  protected readonly options = computed(() => {
    const main = this.display.main();
    const current = this.display.current();
    const list = this.currencies.currencies();
    if (list.length === 0) {
      return [{ code: current, label: current }];
    }

    const sorted = [...list].sort((a, b) =>
      a.code === main ? -1 : b.code === main ? 1 : a.code.localeCompare(b.code),
    );
    return sorted.map((c) => ({
      code: c.code,
      label: c.code === main ? `${c.code}, ${c.name} (main)` : `${c.code}, ${c.name}`,
    }));
  });

  constructor() {
    void this.currencies.load();
  }

  protected pick(event: Event): void {
    this.display.show((event.target as HTMLSelectElement).value);
  }
}
