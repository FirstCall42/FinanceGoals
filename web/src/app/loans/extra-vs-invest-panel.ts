import { Component, computed, inject, input, signal } from '@angular/core';
import { MoneyPipe } from '../money/money.pipe';
import { ComparisonResult, compareExtraAndInvest } from './extra-vs-invest';
import { LoanCalculatorService } from './loan-calculator.service';
import { LoanInput, parseNumber, toRequest } from './loan-input';

const width = 640;
const height = 240;
const pad = { left: 8, right: 8, top: 22, bottom: 20 };

/**
 * #120: the loan above, with the same monthly amount going either to extra payments or to a fund.
 * Uses the loan as it stands in the form (its own extra payments are left out) and the same
 * calculation as the calculator, so the schedules match.
 */
@Component({
  selector: 'app-extra-vs-invest',
  imports: [MoneyPipe],
  template: `
    <section class="panel">
      <h2>Extra payments or investing?</h2>
      <p class="hint">
        The same amount every month, either as extra payments on this loan or into a fund. When the
        loan ends, what it asked each month goes to the fund. Net worth is the fund minus what is
        still owed.
      </p>
      <form (submit)="compare($event)">
        <div class="grid">
          <label class="field">
            <span>Monthly amount</span>
            <input inputmode="decimal" [value]="amount()" (input)="amount.set(text($event))" />
          </label>
          <label class="field">
            <span>Fund return per year (%)</span>
            <input
              inputmode="decimal"
              [value]="fundReturn()"
              (input)="fundReturn.set(text($event))"
            />
          </label>
          <label class="field">
            <span>Interest deduction (%)</span>
            <input
              inputmode="decimal"
              [value]="deduction()"
              (input)="deduction.set(text($event))"
            />
          </label>
          <label class="field">
            <span>Horizon (years)</span>
            <input inputmode="decimal" [value]="horizon()" (input)="horizon.set(text($event))" />
          </label>
        </div>
        <button type="submit" [disabled]="busy()">Compare</button>
      </form>
      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
      }
      @if (result(); as r) {
        <p>
          The loan is paid off {{ r.standardPayoff }} without extra payments and
          {{ r.extraPayoff }} with them. The return is a guess, so the low and high cases are
          {{ spread }} percentage points either side. Positive means extra payments come out ahead.
        </p>
        @if (shape(); as s) {
          <svg
            [attr.viewBox]="'0 0 ' + w + ' ' + h"
            role="img"
            aria-label="Net worth over time: extra payments against investing"
          >
            <line
              [attr.x1]="8"
              [attr.x2]="w - 8"
              [attr.y1]="s.zero"
              [attr.y2]="s.zero"
              class="axis"
            />
            <polyline class="invest" [attr.points]="s.invest" />
            <polyline class="extra" [attr.points]="s.extra" />
          </svg>
          <p class="legend">
            <span class="key invest"></span> Investing <span class="key extra"></span> Extra
            payments
          </p>
        }
        <div class="table-wrap">
          <table>
            <thead>
              <tr>
                <th scope="col">Return</th>
                @for (a of r.cases[0].after; track a.years) {
                  <th scope="col">After {{ a.years }} years</th>
                }
                <th scope="col">At payoff</th>
              </tr>
            </thead>
            <tbody>
              @for (c of r.cases; track c.label) {
                <tr>
                  <th scope="row">{{ c.label }} ({{ c.yearlyPercent }} %)</th>
                  @for (a of c.after; track a.years) {
                    <td class="fg-amount">{{ a.difference | money: currency() : minorUnits() }}</td>
                  }
                  <td class="fg-amount">{{ c.atPayoff | money: currency() : minorUnits() }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </section>
  `,
  styles: `
    .hint,
    .legend {
      color: var(--fg-muted);
      font-size: 13px;
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
      gap: 12px;
      margin-bottom: 12px;
    }
    svg {
      width: 100%;
      height: auto;
      max-height: 280px;
    }
    .axis {
      stroke: var(--fg-line);
    }
    polyline {
      fill: none;
      stroke-width: 2.5;
      stroke-linejoin: round;
    }
    .invest {
      stroke: var(--fg-muted);
      stroke-dasharray: 6 4;
    }
    .extra {
      stroke: var(--fg-accent);
    }
    .key {
      display: inline-block;
      width: 14px;
      height: 0;
      vertical-align: middle;
      margin: 0 4px 0 10px;
    }
    .key:first-child {
      margin-left: 0;
    }
    .key.invest {
      border-top: 3px dashed var(--fg-muted);
    }
    .key.extra {
      border-top: 3px solid var(--fg-accent);
    }
  `,
})
export class ExtraVsInvest {
  private readonly calculator = inject(LoanCalculatorService);

  readonly loan = input.required<LoanInput>();
  readonly minorUnits = input(2);

  protected readonly amount = signal('5 000');
  protected readonly fundReturn = signal('6');
  protected readonly deduction = signal('22');
  protected readonly horizon = signal('20');
  protected readonly busy = signal(false);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly result = signal<ComparisonResult | undefined>(undefined);
  protected readonly currency = computed(() => this.loan().currency);
  protected readonly spread = 2;
  protected readonly w = width;
  protected readonly h = height;

  protected readonly shape = computed(() => {
    const points = this.result()?.points ?? [];
    if (points.length < 2) {
      return undefined;
    }
    const values = points.flatMap((p) => [p.extraPayments, p.invest, 0]);
    const lo = Math.min(...values);
    const hi = Math.max(...values) || 1;
    const x = (i: number) => pad.left + (i / (points.length - 1)) * (width - pad.left - pad.right);
    const y = (v: number) =>
      pad.top + (1 - (v - lo) / (hi - lo || 1)) * (height - pad.top - pad.bottom);
    const line = (pick: (p: (typeof points)[number]) => number) =>
      points.map((p, i) => `${x(i).toFixed(1)},${y(pick(p)).toFixed(1)}`).join(' ');
    return {
      zero: y(0),
      extra: line((p) => p.extraPayments),
      invest: line((p) => p.invest),
    };
  });

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async compare(event: Event): Promise<void> {
    event.preventDefault();
    this.result.set(undefined);
    const amount = parseNumber(this.amount());
    const fundReturn = parseNumber(this.fundReturn());
    const deduction = parseNumber(this.deduction());
    const years = parseNumber(this.horizon());
    if (!(amount > 0) || Number.isNaN(fundReturn) || !(deduction >= 0 && deduction <= 100)) {
      this.error.set('Enter the monthly amount, the return and a deduction between 0 and 100 %.');
      return;
    }
    if (!(years >= 1 && years <= 50)) {
      this.error.set('Enter a horizon between 1 and 50 years.');
      return;
    }

    // The form's loan with only the monthly amount as extra payments.
    const loan = this.loan();
    const built = toRequest(
      {
        ...loan,
        extras: [],
        monthlyAmount: String(amount),
        monthlyFrom: loan.firstPayment,
        monthlyUntil: '',
      },
      this.minorUnits(),
    );
    if ('error' in built) {
      this.error.set(built.error);
      return;
    }

    this.busy.set(true);
    const outcome = await this.calculator.calculate(built.request);
    this.busy.set(false);
    if (!outcome.ok) {
      this.error.set(outcome.error);
      return;
    }

    this.error.set(undefined);
    this.result.set(
      compareExtraAndInvest({
        standard: outcome.result.withoutExtraSchedule,
        withExtra: outcome.result.withExtraSchedule,
        amount,
        returnPercent: fundReturn,
        taxDeductionPercent: deduction,
        horizonYears: years,
        minorUnits: this.minorUnits(),
      }),
    );
  }
}
