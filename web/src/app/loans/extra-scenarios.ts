import { Component, computed, inject, input, signal } from '@angular/core';
import { formatDate } from '../date/date-field';
import { MoneyPipe } from '../money/money.pipe';
import {
  LoanCalculatorService,
  LoanCalculationResult,
  ScheduleRow,
} from './loan-calculator.service';
import { LoanInput, formatMonths, isoDate, parseNumber, toRequest } from './loan-input';

const width = 640;
const height = 260;
const pad = { left: 8, right: 8, top: 22, bottom: 20 };
const maxScenarios = 4;

export interface Scenario {
  /** 0 is the loan as it stands, with no extra payments. */
  readonly amount: number;
  readonly lastPayment: string;
  readonly payments: number;
  readonly totalInterest: number;
  readonly monthsSaved: number;
  readonly interestSaved: number;
  readonly schedule: readonly ScheduleRow[];
}

interface Outcome {
  readonly currency: string;
  readonly units: number;
  readonly scenarios: readonly Scenario[];
}

/** The scenarios of one calculation each: the first is the loan without extras, then one per amount. */
export function toScenarios(
  results: readonly { amount: number; result: LoanCalculationResult }[],
): Scenario[] {
  if (results.length === 0) {
    return [];
  }
  const base = results[0].result;
  return [
    {
      amount: 0,
      lastPayment: base.withoutExtra.lastPayment,
      payments: base.withoutExtra.payments,
      totalInterest: base.withoutExtra.totalInterest,
      monthsSaved: 0,
      interestSaved: 0,
      schedule: base.withoutExtraSchedule,
    },
    ...results.map(({ amount, result }) => ({
      amount,
      lastPayment: result.withExtra.lastPayment,
      payments: result.withExtra.payments,
      totalInterest: result.withExtra.totalInterest,
      monthsSaved: result.monthsSaved,
      interestSaved: result.interestSaved,
      schedule: result.withExtraSchedule,
    })),
  ];
}

/**
 * What if the same extra amount were paid every month from now on? The loan in the form (opened from
 * its latest recorded balance, if it is a saved one) is calculated with each amount as a monthly extra
 * and set against paying nothing extra. Extra payments entered in the form are left out, so the
 * scenarios differ only by the monthly amount. Nothing is stored.
 */
@Component({
  selector: 'app-extra-scenarios',
  imports: [MoneyPipe],
  template: `
    <section class="panel" aria-labelledby="scenarios-title">
      <h2 id="scenarios-title">What if I pay extra every month?</h2>
      <p class="hint">
        Each amount is paid on top of the normal payment every month from the next payment until the
        loan is gone, with the rates in the form. Extra payments entered in the form are left out.
      </p>
      <form (submit)="compare($event)">
        <div class="amounts">
          @for (amount of amounts(); track $index; let i = $index) {
            <label class="field">
              <span>Extra per month {{ i + 1 }}</span>
              <input
                inputmode="decimal"
                [value]="amount"
                (input)="update(i, text($event))"
                [attr.aria-label]="'Monthly extra ' + (i + 1)"
              />
            </label>
            @if (amounts().length > 1) {
              <button
                type="button"
                class="remove"
                (click)="remove(i)"
                [attr.aria-label]="'Remove monthly extra ' + (i + 1)"
              >
                Remove
              </button>
            }
          }
        </div>
        <div class="actions">
          @if (amounts().length < max) {
            <button type="button" (click)="add()">Add amount</button>
          }
          <button type="submit" [disabled]="busy()">Compare</button>
        </div>
      </form>
      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
      }
      @if (outcome(); as o) {
        @if (shape(); as s) {
          <svg
            [attr.viewBox]="'0 0 ' + w + ' ' + h"
            role="img"
            [attr.aria-label]="
              'Remaining balance in ' + o.currency + ' for each monthly extra amount'
            "
          >
            <line
              class="axis"
              [attr.x1]="pad.left"
              [attr.x2]="w - pad.right"
              [attr.y1]="s.zero"
              [attr.y2]="s.zero"
            />
            @for (line of s.lines; track line.index) {
              <polyline [class]="'line c' + line.index" [attr.points]="line.points" />
            }
            <text class="tick" [attr.x]="pad.left" [attr.y]="12">{{ s.top }}</text>
            <text class="tick" [attr.x]="pad.left" [attr.y]="h - 4">{{ s.from }}</text>
            <text class="tick" text-anchor="end" [attr.x]="w - pad.right" [attr.y]="h - 4">
              {{ s.to }}
            </text>
          </svg>
        }
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Extra per month</th>
                <th scope="col">Paid off</th>
                <th scope="col">Time saved</th>
                <th scope="col">Total interest</th>
                <th scope="col">Interest saved</th>
              </tr>
            </thead>
            <tbody>
              @for (sc of o.scenarios; track $index; let i = $index) {
                <tr>
                  <th scope="row">
                    <span [class]="'key c' + i"></span>
                    @if (sc.amount === 0) {
                      None
                    } @else {
                      <span class="fg-amount">{{ sc.amount | money: o.currency : o.units }}</span>
                    }
                  </th>
                  <td>{{ date(sc.lastPayment) }}</td>
                  <td>{{ sc.monthsSaved === 0 ? '–' : months(sc.monthsSaved) }}</td>
                  <td class="fg-amount">{{ sc.totalInterest | money: o.currency : o.units }}</td>
                  <td class="fg-amount">
                    {{
                      sc.interestSaved === 0
                        ? '–'
                        : (sc.interestSaved | money: o.currency : o.units)
                    }}
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styles: `
    .hint {
      color: var(--fg-muted);
      font-size: 13px;
    }
    .amounts {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
      gap: 12px;
      align-items: end;
      margin-bottom: 12px;
    }
    .actions {
      display: flex;
      gap: 8px;
      flex-wrap: wrap;
    }
    svg {
      width: 100%;
      height: auto;
      max-height: 300px;
    }
    .axis {
      stroke: var(--fg-line);
    }
    polyline {
      fill: none;
      stroke-width: 2.5;
      stroke-linejoin: round;
    }
    .tick {
      fill: var(--fg-muted);
      font-size: 11px;
    }
    .c0 {
      stroke: var(--fg-muted);
      stroke-dasharray: 6 4;
    }
    .c1 {
      stroke: var(--fg-accent);
    }
    .c2 {
      stroke: var(--fg-negative);
    }
    .c3 {
      stroke: var(--fg-warn-ink);
    }
    .c4 {
      stroke: var(--fg-ink);
    }
    .key {
      display: inline-block;
      width: 14px;
      height: 0;
      vertical-align: middle;
      margin-right: 6px;
      border-top: 3px solid;
    }
    .key.c0 {
      border-top-style: dashed;
      border-color: var(--fg-muted);
    }
    .key.c1 {
      border-color: var(--fg-accent);
    }
    .key.c2 {
      border-color: var(--fg-negative);
    }
    .key.c3 {
      border-color: var(--fg-warn-ink);
    }
    .key.c4 {
      border-color: var(--fg-ink);
    }
  `,
})
export class ExtraScenarios {
  private readonly calculator = inject(LoanCalculatorService);

  readonly loan = input.required<LoanInput>();
  readonly minorUnits = input(2);

  protected readonly amounts = signal<readonly string[]>(['10 000', '18 500']);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | undefined>(undefined);
  /** With the currency it was made in, so it stays right when the form changes. */
  protected readonly outcome = signal<Outcome | undefined>(undefined);
  protected readonly max = maxScenarios;
  protected readonly w = width;
  protected readonly h = height;
  protected readonly pad = pad;

  protected readonly shape = computed(() => {
    const scenarios = this.outcome()?.scenarios ?? [];
    const curves = scenarios.map((s) => curve(s.schedule));
    if (curves.length === 0 || curves.some((c) => c.length < 2)) {
      return undefined;
    }
    const t0 = Math.min(...curves.map((c) => c[0].time));
    const t1 = Math.max(...curves.map((c) => c[c.length - 1].time));
    const top = Math.max(...curves.flatMap((c) => c.map((p) => p.balance))) || 1;
    const x = (t: number) =>
      pad.left + ((t - t0) / (t1 - t0 || 1)) * (width - pad.left - pad.right);
    const y = (v: number) => pad.top + (1 - v / top) * (height - pad.top - pad.bottom);
    return {
      zero: y(0),
      lines: curves.map((c, index) => ({
        index,
        points: c.map((p) => `${x(p.time).toFixed(1)},${y(p.balance).toFixed(1)}`).join(' '),
      })),
      top: `${Math.round(top).toLocaleString('nb-NO')} ${this.outcome()?.currency ?? ''}`,
      from: monthYear(t0),
      to: monthYear(t1),
    };
  });

  protected date(value: string): string {
    return formatDate(value);
  }

  protected months(value: number): string {
    return formatMonths(value);
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected update(index: number, value: string): void {
    this.amounts.update((list) => list.map((a, i) => (i === index ? value : a)));
  }

  protected add(): void {
    this.amounts.update((list) => [...list, '']);
  }

  protected remove(index: number): void {
    this.amounts.update((list) => list.filter((_, i) => i !== index));
  }

  protected async compare(event: Event): Promise<void> {
    event.preventDefault();
    this.outcome.set(undefined);
    const parsed = this.amounts().map(parseNumber);
    if (parsed.some((a) => !(a > 0))) {
      this.error.set('Enter each monthly extra as an amount above zero, for example 10 000.');
      return;
    }
    const amounts = [...new Set(parsed)];

    // From today (or the first payment, if later): what has been paid is done.
    const loan = this.loan();
    const today = isoDate(new Date());
    const from = loan.firstPayment > today ? loan.firstPayment : today;
    const requests = [];
    for (const amount of amounts) {
      const built = toRequest(
        { ...loan, extras: [], monthlyAmount: String(amount), monthlyFrom: from, monthlyUntil: '' },
        this.minorUnits(),
      );
      if ('error' in built) {
        this.error.set(built.error);
        return;
      }
      requests.push({ amount, request: built.request });
    }

    this.busy.set(true);
    const outcomes = await Promise.all(
      requests.map(async ({ amount, request }) => ({
        amount,
        outcome: await this.calculator.calculate(request),
      })),
    );
    this.busy.set(false);
    const failed = outcomes.find((o) => !o.outcome.ok);
    if (failed && !failed.outcome.ok) {
      this.error.set(failed.outcome.error);
      return;
    }

    this.error.set(undefined);
    this.outcome.set({
      currency: loan.currency,
      units: this.minorUnits(),
      scenarios: toScenarios(
        outcomes.flatMap((o) =>
          o.outcome.ok ? [{ amount: o.amount, result: o.outcome.result }] : [],
        ),
      ),
    });
  }
}

interface Point {
  readonly time: number;
  readonly balance: number;
}

/** The balance after each payment, starting from what was owed before the first. */
function curve(rows: readonly ScheduleRow[]): Point[] {
  if (rows.length === 0) {
    return [];
  }
  const time = (r: ScheduleRow) => Date.parse(`${r.date}T00:00:00Z`);
  return [
    { time: time(rows[0]), balance: rows[0].openingBalance },
    ...rows.map((r) => ({ time: time(r), balance: r.balance })),
  ];
}

function monthYear(time: number): string {
  return new Date(time).toLocaleDateString('nb-NO', {
    month: '2-digit',
    year: 'numeric',
    timeZone: 'UTC',
  });
}
