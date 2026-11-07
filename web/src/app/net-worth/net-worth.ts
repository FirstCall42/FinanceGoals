import { Component, computed, effect, inject, signal } from '@angular/core';
import { DateField, formatDate } from '../date/date-field';
import { accountTypes } from '../accounts/account.service';
import { AuthService } from '../auth/auth.service';
import { CurrencyService } from '../currency/currency.service';
import { DisplayCurrencyService } from '../currency/display-currency.service';
import { MissingRate } from '../money/missing-rate';
import { MoneyPipe } from '../money/money.pipe';
import { messageOf } from '../profile/profile.service';
import { NetWorthChart } from './net-worth-chart';
import { Forecast, NetWorth, NetWorthPoint, NetWorthService } from './net-worth.service';

/**
 * Net worth (#37): the accounts' balances on a date, converted to the currency picked with the
 * switcher, and a monthly history. Total and liquid net worth: property and pensions aren't liquid.
 * A currency without an exchange rate is named in a warning and its accounts are left out of the
 * totals; a missing rate is never counted as 1.
 */
@Component({
  selector: 'app-net-worth',
  imports: [DateField, MoneyPipe, MissingRate, NetWorthChart],
  template: `
    <section class="panel" aria-labelledby="nw-title">
      <h1 id="nw-title">Net worth</h1>
      @if (!profile()) {
        <p>Sign in to see your net worth.</p>
      } @else {
        <div class="field date">
          <span aria-hidden="true">As of</span
          ><app-date-field
            [value]="date()"
            [max]="today"
            (valueChange)="date.set($event)"
            label="As of"
          />
        </div>

        @if (error(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }

        @if (current(); as nw) {
          @if (nw.missingRates.length > 0) {
            <app-missing-rate [currencyCodes]="nw.missingRates" [date]="nw.date" />
            <p class="note">
              The totals leave out the accounts in {{ nw.missingRates.join(' and ') }} until a rate
              exists.
            </p>
          }

          <div class="tiles">
            <div class="tile">
              <span class="label">Net worth</span>
              <strong class="fg-amount">{{ nw.total | money: nw.currency : units() }}</strong>
              <small
                >Accounts minus loans{{
                  nw.missingRates.length > 0 ? ', without the ones missing a rate' : ''
                }}</small
              >
            </div>
            <div class="tile">
              <span class="label">Liquid net worth</span>
              <strong class="fg-amount">{{ nw.liquid | money: nw.currency : units() }}</strong>
              <small>Leaves out property, pensions and loans secured on property</small>
            </div>
          </div>

          @if (nw.loansWithoutBalance.length > 0) {
            <p class="note" role="alert">
              {{ nw.loansWithoutBalance.join(', ') }} not counted: no balance is recorded for
              {{ nw.loansWithoutBalance.length === 1 ? 'it' : 'them' }} yet, so the totals are too
              high by what {{ nw.loansWithoutBalance.length === 1 ? 'it owes' : 'they owe' }}.
            </p>
          }

          @if (nw.lines.length === 0) {
            <p class="empty">
              No balances recorded on or before this date. Record some under Balances.
            </p>
          } @else {
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">Account</th>
                    <th scope="col">Balance</th>
                    <th scope="col">In {{ nw.currency }}</th>
                    <th scope="col">Balance date</th>
                  </tr>
                </thead>
                <tbody>
                  @for (line of nw.lines; track line.accountId) {
                    <tr>
                      <td>
                        {{ line.name }}
                        <small class="type">
                          {{ typeLabel(line.type) }}{{ line.liquid ? '' : ' · not liquid' }}
                        </small>
                      </td>
                      <td class="fg-amount">
                        {{ line.amount | money: line.currencyCode : minorUnits(line.currencyCode) }}
                      </td>
                      <td class="fg-amount">
                        @if (line.converted !== null) {
                          {{ line.converted | money: nw.currency : units() }}
                        } @else {
                          <span class="norate">no rate</span>
                        }
                      </td>
                      <td>{{ formatDate(line.balanceDate) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (rateDate(); as rate) {
              <p class="note">
                Converted with exchange rates from {{ formatDate(rate) }} or earlier.
              </p>
            }
          }

          @if (nw.debts.length > 0) {
            <h2>Loans</h2>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">Loan</th>
                    <th scope="col">Owed</th>
                    <th scope="col">In {{ nw.currency }}</th>
                    <th scope="col">Balance date</th>
                  </tr>
                </thead>
                <tbody>
                  @for (debt of nw.debts; track debt.loanId) {
                    <tr>
                      <td>
                        {{ debt.name }}
                        <small class="type">{{
                          debt.securedOnProperty ? 'secured on property · not in liquid' : ''
                        }}</small>
                      </td>
                      <td class="fg-amount">
                        {{ debt.amount | money: debt.currencyCode : minorUnits(debt.currencyCode) }}
                      </td>
                      <td class="fg-amount">
                        @if (debt.converted !== null) {
                          {{ debt.converted | money: nw.currency : units() }}
                        } @else {
                          <span class="norate">no rate</span>
                        }
                      </td>
                      <td>{{ formatDate(debt.balanceDate) }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }

          @if (history().length > 0) {
            <h2>Month by month</h2>
            <label class="horizon">
              <span>Forecast</span>
              <select (change)="horizon.set(+text($event))">
                @for (years of horizons; track years) {
                  <option [value]="years" [selected]="years === horizon()">
                    {{ years === 0 ? 'None' : years + ' years' }}
                  </option>
                }
              </select>
            </label>
            <app-net-worth-chart
              [points]="history()"
              [currency]="nw.currency"
              [forecast]="shownForecast()?.points ?? []"
            />
            @if (shownForecast(); as f) {
              @if (f.missingRates.length > 0) {
                <app-missing-rate [currencyCodes]="f.missingRates" [date]="f.from" />
                <p class="note">
                  The forecast leaves out the accounts in {{ f.missingRates.join(' and ') }} until a
                  rate exists.
                </p>
              }
              @if (f.capped) {
                <p class="note">An amount in the forecast hit the upper limit and stops there.</p>
              }
              <p class="note">
                The forecast grows each account from its latest balance with the return and monthly
                contribution set under Accounts, at today's exchange rates. The low and high lines
                are {{ f.spreadPoints }} percentage points either side. What the loans owe today is
                subtracted and held flat.
              </p>
            }
            @if (forecast() && !shownForecast()) {
              <p class="note">
                The forecast starts today, so it is left out while another date is picked.
              </p>
            }
            @if (forecastError(); as message) {
              <p class="error" role="alert">{{ message }}</p>
            }
            @if (historyMissing(); as missing) {
              <app-missing-rate [currencyCodes]="missing.codes" [date]="missing.from" />
              <p class="note">
                {{
                  missing.from === missing.to
                    ? 'The month ' + formatDate(missing.from)
                    : 'The months from ' +
                      formatDate(missing.from) +
                      ' to ' +
                      formatDate(missing.to)
                }}
                leave out the accounts in {{ missing.codes.join(' and ') }}, so the lines are too
                low for those months.
              </p>
            }
            @if (historyRateDate(); as rate) {
              <p class="note">
                The history is converted with each month's rates; the oldest used are from
                {{ formatDate(rate) }}.
              </p>
            }
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th scope="col">Date</th>
                    <th scope="col">Net worth</th>
                    <th scope="col">Liquid</th>
                  </tr>
                </thead>
                <tbody>
                  @for (p of history(); track p.date) {
                    <tr>
                      <td>{{ formatDate(p.date) }}</td>
                      <td class="fg-amount">
                        {{ p.total | money: nw.currency : units() }}
                        @if (p.missingRates.length > 0) {
                          <span class="norate" [title]="'No rate for ' + p.missingRates.join(', ')">
                            · excl. {{ p.missingRates.join(', ') }}
                          </span>
                        }
                      </td>
                      <td class="fg-amount">{{ p.liquid | money: nw.currency : units() }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
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
      gap: 16px;
      max-width: 860px;
    }
    h1 {
      margin: 0;
      font-size: 20px;
    }
    h2 {
      margin: 8px 0 0;
      font-size: 16px;
    }
    p {
      margin: 0;
    }
    .note,
    .empty,
    .type {
      color: var(--fg-muted);
    }
    .type {
      display: block;
      font-size: 12px;
    }
    .norate {
      color: var(--fg-warn-ink);
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
    input {
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
    .tiles {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(240px, 1fr));
      gap: 12px;
    }
    .tile {
      border: 1px solid var(--fg-line);
      border-radius: var(--fg-radius);
      padding: 16px;
      display: flex;
      flex-direction: column;
      gap: 4px;
    }
    .tile .label,
    .tile small {
      color: var(--fg-muted);
      font-size: 13px;
    }
    .tile strong {
      font-size: 24px;
    }
    .table-wrap {
      overflow-x: auto;
    }
    table {
      border-collapse: collapse;
      width: 100%;
      font-size: 14px;
    }
    th,
    td {
      padding: 8px 10px;
      text-align: right;
      border-bottom: 1px solid var(--fg-line);
    }
    th:first-child,
    td:first-child {
      text-align: left;
    }
    th {
      color: var(--fg-muted);
      font-weight: 500;
      white-space: nowrap;
    }
    .error {
      padding: 10px 12px;
      border-radius: 10px;
      background: var(--fg-warn-bg);
      color: var(--fg-warn-ink);
      border: 1px solid var(--fg-warn-line);
    }
  `,
})
export class NetWorthPage {
  private readonly auth = inject(AuthService);
  private readonly service = inject(NetWorthService);
  private readonly display = inject(DisplayCurrencyService);
  private readonly currencies = inject(CurrencyService);

  protected readonly today = isoToday();
  protected readonly profile = computed(() => this.auth.profiles()[0]);
  protected readonly date = signal(isoToday());
  protected readonly current = signal<NetWorth | undefined>(undefined);
  protected readonly history = signal<readonly NetWorthPoint[]>([]);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly horizons = [0, 5, 10, 20, 30];
  protected readonly horizon = signal(10);
  protected readonly forecast = signal<Forecast | undefined>(undefined);
  protected readonly forecastError = signal<string | undefined>(undefined);
  /** The forecast continues the history only when the history ends today, where the forecast starts. */
  protected readonly shownForecast = computed(() =>
    this.date() === isoToday() ? this.forecast() : undefined,
  );
  private forecasts = 0;

  /** The oldest exchange rate behind any converted line, to say which day's rates the figures use. */
  protected readonly rateDate = computed(() => {
    const now = this.current();
    const dates = [...(now?.lines ?? []), ...(now?.debts ?? [])]
      .map((l) => l.rateDate)
      .filter((d) => d !== null);
    return dates.length > 0 ? dates.reduce((a, b) => (a < b ? a : b)) : undefined;
  });

  /** Currencies missing a rate in any month of the history, and the first such month. */
  protected readonly historyMissing = computed(() => {
    const bad = this.history().filter((p) => p.missingRates.length > 0);
    return bad.length > 0
      ? {
          codes: [...new Set(bad.flatMap((p) => p.missingRates))].sort(),
          from: bad[0].date,
          to: bad[bad.length - 1].date,
        }
      : undefined;
  });

  protected readonly historyRateDate = computed(() => {
    const dates = this.history()
      .map((p) => p.rateDate)
      .filter((d) => d !== null);
    return dates.length > 0 ? dates.reduce((a, b) => (a < b ? a : b)) : undefined;
  });

  private requests = 0;

  protected readonly units = computed(() =>
    this.minorUnits(this.current()?.currency ?? this.display.current()),
  );

  constructor() {
    void this.currencies.load();
    // The forecast starts today whatever date is picked, so only the household, currency and horizon matter.
    effect(() => {
      const p = this.profile();
      const currency = this.display.current();
      const years = this.horizon();
      if (p) {
        void this.loadForecast(p.id, currency, years);
      }
    });
    // Reloads when the household, the date or the currency picked with the switcher changes.
    effect(() => {
      const p = this.profile();
      const currency = this.display.current();
      const date = this.date();
      if (p && date) {
        void this.load(p.id, currency, date);
      }
    });
  }

  private async loadForecast(profileId: number, currency: string, years: number): Promise<void> {
    const request = ++this.forecasts;
    if (years === 0) {
      this.forecast.set(undefined);
      this.forecastError.set(undefined);
      return;
    }
    try {
      const forecast = await this.service.forecast(profileId, currency, years * 12);
      if (request === this.forecasts) {
        this.forecast.set(forecast);
        this.forecastError.set(undefined);
      }
    } catch (error) {
      if (request === this.forecasts) {
        this.forecast.set(undefined);
        this.forecastError.set(messageOf(error));
      }
    }
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected minorUnits(code: string): number {
    return this.currencies.currencies().find((c) => c.code === code)?.minorUnits ?? 2;
  }

  protected typeLabel(type: string): string {
    return accountTypes.find((t) => t.value === type)?.label ?? type;
  }

  protected formatDate(iso: string): string {
    return formatDate(iso);
  }

  private async load(profileId: number, currency: string, date: string): Promise<void> {
    // Only the latest request may show its result; a slower, older one is dropped.
    const request = ++this.requests;
    try {
      const [current, history] = await Promise.all([
        this.service.on(profileId, currency, date),
        this.service.history(profileId, currency, date),
      ]);
      if (request === this.requests) {
        this.current.set(current);
        this.history.set(history);
        this.error.set(undefined);
      }
    } catch (error) {
      if (request === this.requests) {
        // No old figures beside the error.
        this.current.set(undefined);
        this.history.set([]);
        this.error.set(messageOf(error));
      }
    }
  }
}

function isoToday(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}
