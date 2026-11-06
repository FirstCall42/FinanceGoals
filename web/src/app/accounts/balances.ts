import { Component, computed, effect, inject, signal } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { CurrencyService } from '../currency/currency.service';
import { parseNumber } from '../loans/loan-input';
import { MoneyPipe } from '../money/money.pipe';
import { messageOf } from '../profile/profile.service';
import { Account, AccountService } from './account.service';
import { Balance, BalanceService } from './balance.service';

interface DayGroup {
  readonly date: string;
  readonly rows: readonly { balance: Balance; account: Account }[];
}

/**
 * Balances (#36): record a snapshot of all accounts on one date, and browse the history. An account
 * left blank is not recorded; its latest earlier balance still stands. Choosing a date that already
 * has a snapshot shows its balances, and saving replaces them.
 */
@Component({
  selector: 'app-balances',
  imports: [MoneyPipe],
  template: `
    <section class="panel" aria-labelledby="balances-title">
      <h1 id="balances-title">Balances</h1>
      @if (!profile()) {
        <p>Sign in to record and see your balances.</p>
      } @else if (accounts().length === 0) {
        <p class="hint">Add your accounts first, then record their balances here.</p>
      } @else {
        <form (submit)="save($event)">
          <h2>Record a snapshot</h2>
          <p class="hint">
            Enter each account's balance on the date, in the account's own currency. Leave an
            account blank to skip it.
          </p>
          <label class="field date">
            <span>Date</span>
            <input type="date" [value]="date()" [max]="today" (input)="date.set(text($event))" />
          </label>
          <div class="accounts">
            @for (a of accounts(); track a.id) {
              <label class="field">
                <span>{{ a.name }} · {{ a.currencyCode }}</span>
                <input
                  inputmode="decimal"
                  autocomplete="off"
                  [value]="amounts()[a.id] ?? ''"
                  [placeholder]="lastKnown(a)"
                  (input)="setAmount(a.id, text($event))"
                />
              </label>
            }
          </div>
          <button type="submit" class="primary" [disabled]="busy()">Save snapshot</button>
          @if (saved(); as message) {
            <p class="ok" role="status">{{ message }}</p>
          }
        </form>

        @if (error(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }

        <div class="history">
          <div class="history-head">
            <h2>History</h2>
            <label class="field filter">
              <span>Show</span>
              <select (change)="filter.set(text($event))">
                <option value="" [selected]="filter() === ''">All accounts</option>
                @for (a of allAccounts(); track a.id) {
                  <option [value]="a.id" [selected]="filter() === '' + a.id">
                    {{ a.name }}{{ a.archived ? ' (archived)' : '' }}
                  </option>
                }
              </select>
            </label>
          </div>
          @if (days().length === 0) {
            <p class="empty">No balances recorded yet.</p>
          }
          @for (day of days(); track day.date) {
            <div class="day">
              <div class="day-head">
                <strong>{{ formatDate(day.date) }}</strong>
                @if (filter() === '') {
                  <button type="button" class="link" (click)="removeSnapshot(day.date)">
                    Delete this snapshot
                  </button>
                }
              </div>
              <ul>
                @for (row of day.rows; track row.balance.accountId) {
                  <li>
                    <span class="name">{{ row.account.name }}</span>
                    <span class="amount fg-amount">
                      {{
                        row.balance.amount
                          | money: row.account.currencyCode : minorUnits(row.account)
                      }}
                    </span>
                    <button
                      type="button"
                      class="link"
                      (click)="removeBalance(row.balance)"
                      [attr.aria-label]="
                        'Delete ' + row.account.name + ' on ' + formatDate(day.date)
                      "
                    >
                      Delete
                    </button>
                  </li>
                }
              </ul>
            </div>
          }
        </div>
      }
    </section>
  `,
  styles: `
    .panel {
      background: var(--fg-surface);
      border: 1px solid var(--fg-line);
      border-radius: var(--fg-radius);
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 18px;
      max-width: 860px;
    }
    h1 {
      margin: 0;
      font-size: 20px;
    }
    h2 {
      margin: 0;
      font-size: 16px;
    }
    p {
      margin: 0;
    }
    form {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .hint,
    .empty {
      color: var(--fg-muted);
    }
    .accounts {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
      gap: 12px;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 13px;
      color: var(--fg-muted);
    }
    .field.date {
      max-width: 220px;
    }
    input,
    select {
      min-height: 44px;
      padding: 0 10px;
      border-radius: 10px;
      border: 1px solid var(--fg-line);
      background: var(--fg-surface);
      color: var(--fg-ink);
      font: inherit;
      font-size: 15px;
      box-sizing: border-box;
      width: 100%;
    }
    button {
      font: inherit;
      cursor: pointer;
    }
    .primary {
      align-self: flex-start;
      min-height: 44px;
      padding: 0 16px;
      border-radius: 10px;
      border: 0;
      background: var(--fg-accent);
      color: var(--fg-on-accent);
      font-weight: 600;
    }
    .link {
      background: none;
      border: 0;
      padding: 8px 4px;
      color: var(--fg-accent);
      text-decoration: underline;
    }
    .ok {
      color: var(--fg-accent);
    }
    .error {
      padding: 10px 12px;
      border-radius: 10px;
      background: var(--fg-warn-bg);
      color: var(--fg-warn-ink);
      border: 1px solid var(--fg-warn-line);
    }
    .history {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
    .history-head,
    .day-head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }
    .filter {
      min-width: 220px;
    }
    .day {
      border-top: 1px solid var(--fg-line);
      padding-top: 8px;
    }
    .day ul {
      list-style: none;
      margin: 4px 0 0;
      padding: 0;
    }
    .day li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px 12px;
      padding: 2px 0;
    }
    .name {
      flex: 1 1 160px;
    }
  `,
})
export class Balances {
  private readonly auth = inject(AuthService);
  private readonly accountService = inject(AccountService);
  private readonly balanceService = inject(BalanceService);
  private readonly currencies = inject(CurrencyService);

  protected readonly today = isoToday();
  protected readonly profile = computed(() => this.auth.profiles()[0]);
  protected readonly allAccounts = signal<readonly Account[]>([]);
  protected readonly accounts = computed(() => this.allAccounts().filter((a) => !a.archived));
  protected readonly balances = signal<readonly Balance[]>([]);
  protected readonly date = signal(isoToday());
  protected readonly amounts = signal<Record<number, string>>({});
  protected readonly filter = signal('');
  protected readonly busy = signal(false);
  protected readonly saved = signal<string | undefined>(undefined);
  protected readonly error = signal<string | undefined>(undefined);

  protected readonly days = computed<readonly DayGroup[]>(() => {
    const byAccount = new Map(this.allAccounts().map((a) => [a.id, a]));
    const only = this.filter();
    const groups = new Map<string, { balance: Balance; account: Account }[]>();
    for (const balance of this.balances()) {
      const account = byAccount.get(balance.accountId);
      if (!account || (only !== '' && String(balance.accountId) !== only)) {
        continue;
      }
      groups.set(balance.date, [...(groups.get(balance.date) ?? []), { balance, account }]);
    }
    return [...groups]
      .sort(([a], [b]) => b.localeCompare(a))
      .map(([date, rows]) => ({
        date,
        rows: rows.sort((a, b) => a.account.name.localeCompare(b.account.name)),
      }));
  });

  constructor() {
    void this.currencies.load();
    effect(() => {
      const p = this.profile();
      if (p) {
        void this.load(p.id);
      }
    });
    // Choosing a date that has a snapshot shows its balances, so a correction is a re-save.
    effect(() => {
      const day = this.date();
      const onDay: Record<number, string> = {};
      for (const b of this.balances()) {
        if (b.date === day) {
          onDay[b.accountId] = String(b.amount).replace('.', ',');
        }
      }
      this.amounts.set(onDay);
    });
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected setAmount(accountId: number, value: string): void {
    this.amounts.update((current) => ({ ...current, [accountId]: value }));
  }

  protected minorUnits(account: Account): number {
    return (
      this.currencies.currencies().find((c) => c.code === account.currencyCode)?.minorUnits ?? 2
    );
  }

  /** The newest balance before the chosen date, as a hint of what to type. */
  protected lastKnown(account: Account): string {
    const latest = this.balances()
      .filter((b) => b.accountId === account.id && b.date < this.date())
      .sort((a, b) => b.date.localeCompare(a.date))[0];
    return latest ? `last: ${latest.amount.toLocaleString('nb-NO')}` : '';
  }

  protected formatDate(iso: string): string {
    return new Date(`${iso}T00:00:00Z`).toLocaleDateString('nb-NO', { timeZone: 'UTC' });
  }

  protected async save(event: Event): Promise<void> {
    event.preventDefault();
    const p = this.profile();
    if (!p) {
      return;
    }

    const values: { accountId: number; amount: number }[] = [];
    for (const account of this.accounts()) {
      const typed = (this.amounts()[account.id] ?? '').trim();
      if (typed === '') {
        continue;
      }
      const amount = parseNumber(typed);
      if (Number.isNaN(amount)) {
        this.error.set(`The balance of ${account.name} isn't a number: ${typed}`);
        return;
      }
      values.push({ accountId: account.id, amount });
    }

    if (values.length === 0) {
      this.error.set('Enter the balance of at least one account.');
      return;
    }

    await this.run(async () => {
      await this.balanceService.saveSnapshot(p.id, this.date(), values);
      this.saved.set(
        `Saved ${values.length} balance${values.length === 1 ? '' : 's'} for ${this.formatDate(this.date())}.`,
      );
      await this.load(p.id);
    });
  }

  protected async removeSnapshot(date: string): Promise<void> {
    const p = this.profile();
    if (p && window.confirm(`Delete every balance recorded on ${this.formatDate(date)}?`)) {
      await this.run(async () => {
        await this.balanceService.deleteSnapshot(p.id, date);
        await this.load(p.id);
      });
    }
  }

  protected async removeBalance(balance: Balance): Promise<void> {
    const p = this.profile();
    if (p) {
      await this.run(async () => {
        await this.balanceService.deleteBalance(p.id, balance.accountId, balance.date);
        await this.load(p.id);
      });
    }
  }

  private async load(profileId: number): Promise<void> {
    try {
      const [accounts, balances] = await Promise.all([
        this.accountService.list(profileId, true),
        this.balanceService.list(profileId),
      ]);
      this.allAccounts.set(accounts);
      this.balances.set(balances);
    } catch (error) {
      this.error.set(messageOf(error));
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(undefined);
    this.saved.set(undefined);
    try {
      await action();
    } catch (error) {
      this.error.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }
}

function isoToday(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}
