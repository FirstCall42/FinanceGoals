import { Component, computed, inject, signal } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { CurrencyService } from '../currency/currency.service';
import { DisplayCurrencyService } from '../currency/display-currency.service';
import { HouseholdSettings } from '../profile/household-settings';
import { ProfileService, messageOf } from '../profile/profile.service';

/** Household settings: the main currency (stored on the household profile) and its members. */
@Component({
  selector: 'app-settings',
  imports: [HouseholdSettings],
  template: `
    <section aria-labelledby="settings-title">
      <h1 id="settings-title">Settings</h1>
      <label class="field">
        <span>Main currency</span>
        <select
          [value]="display.main()"
          [disabled]="!canChange() || saving()"
          (change)="pick($event)"
        >
          @for (c of currencies(); track c.code) {
            <option [value]="c.code" [selected]="c.code === display.main()">
              {{ c.code }}, {{ c.name }}
            </option>
          }
        </select>
        <small>
          @if (!auth.signedIn()) {
            Sign in to see and change your household's main currency.
          } @else if (!canChange()) {
            Totals and charts start in this currency. Only an Owner of the household can change it.
          } @else {
            Totals and charts start in this currency, for everyone in the household.
          }
        </small>
      </label>
      @if (failed()) {
        <p role="status">The currency list couldn't be loaded. Try again later.</p>
      }
      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
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
    .error {
      margin: 0;
      padding: 10px 12px;
      border-radius: 10px;
      background: var(--fg-warn-bg);
      color: var(--fg-warn-ink);
      border: 1px solid var(--fg-warn-line);
    }
  `,
})
export class Settings {
  protected readonly auth = inject(AuthService);
  protected readonly display = inject(DisplayCurrencyService);
  private readonly currencyService = inject(CurrencyService);
  private readonly profiles = inject(ProfileService);

  protected readonly saving = signal(false);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly canChange = computed(() => this.auth.profiles()[0]?.role === 'Owner');
  protected readonly currencies = computed(() => {
    const list = this.currencyService.currencies();
    return list.length > 0 ? list : [{ code: this.display.main(), name: '' }];
  });
  protected readonly failed = this.currencyService.failed;

  constructor() {
    void this.currencyService.load();
  }

  protected async pick(event: Event): Promise<void> {
    const profile = this.auth.profiles()[0];
    if (!profile) {
      return;
    }

    const select = event.target as HTMLSelectElement;
    this.saving.set(true);
    this.error.set(undefined);
    try {
      await this.profiles.setMainCurrency(profile.id, select.value);
      await this.auth.refresh();
      this.display.reset();
    } catch (error) {
      // Show what is stored again, not the currency that didn't save.
      select.value = this.display.main();
      this.error.set(messageOf(error));
    } finally {
      this.saving.set(false);
    }
  }
}
