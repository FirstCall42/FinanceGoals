import { Component, computed, effect, inject, signal } from '@angular/core';
import { CurrencyService } from '../currency/currency.service';
import { MoneyPipe } from '../money/money.pipe';
import {
  LoanCalculationResult,
  LoanCalculatorService,
  ScheduleRow,
} from './loan-calculator.service';
import { LoanInput, defaultInput, formatMonths, isoDate, toRequest } from './loan-input';

const storageKey = 'financegoals.loanCalculator';

interface ScheduleYear {
  readonly year: number;
  readonly rows: readonly ScheduleRow[];
  readonly payments: number;
  readonly interest: number;
  readonly extra: number;
  readonly balance: number;
}

/**
 * The mortgage calculator (#44): a variable-rate annuity loan with its rate history and extra
 * payments, and when it is paid off with and without the extras. The API stores nothing; the form
 * is kept in this browser only, so it is there next time.
 */
@Component({
  selector: 'app-loan-calculator',
  imports: [MoneyPipe],
  templateUrl: './loan-calculator.html',
  styleUrl: './loan-calculator.scss',
})
export class LoanCalculator {
  private readonly calculator = inject(LoanCalculatorService);
  private readonly currencyService = inject(CurrencyService);

  protected readonly input = signal<LoanInput>(loadInput());
  protected readonly result = signal<LoanCalculationResult | undefined>(undefined);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly busy = signal(false);
  protected readonly showExtras = signal(true);

  protected readonly currencies = computed(() => {
    const list = this.currencyService.currencies();
    return list.length > 0 ? list : [{ code: 'NOK', minorUnits: 2, name: 'Norwegian Krone' }];
  });
  protected readonly minorUnits = computed(
    () => this.currencies().find((c) => c.code === this.input().currency)?.minorUnits ?? 2,
  );

  /** The payment due next: the first one from today on, in the schedule with extras. */
  protected readonly nextPayment = computed(() => {
    const today = isoDate(new Date());
    return this.result()?.withExtraSchedule.find((row) => row.date >= today);
  });

  protected readonly years = computed<readonly ScheduleYear[]>(() => {
    const result = this.result();
    if (!result) {
      return [];
    }

    const rows = this.showExtras() ? result.withExtraSchedule : result.withoutExtraSchedule;
    const byYear = new Map<number, ScheduleRow[]>();
    for (const row of rows) {
      const year = Number(row.date.slice(0, 4));
      byYear.set(year, [...(byYear.get(year) ?? []), row]);
    }

    return [...byYear].map(([year, list]) => ({
      year,
      rows: list,
      payments: sum(list, (r) => r.payment, this.minorUnits()),
      interest: sum(list, (r) => r.interest, this.minorUnits()),
      extra: sum(list, (r) => r.extra, this.minorUnits()),
      balance: list[list.length - 1].balance,
    }));
  });

  protected readonly formatMonths = formatMonths;

  constructor() {
    void this.currencyService.load();
    effect(() => {
      const value = this.input();
      try {
        localStorage.setItem(storageKey, JSON.stringify(value));
      } catch {
        // Storage can be blocked (private windows); the form then simply starts fresh next time.
      }
    });
  }

  protected set<K extends keyof LoanInput>(key: K, value: LoanInput[K]): void {
    this.input.update((current) => ({ ...current, [key]: value }));
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected addRateChange(): void {
    this.set('rateChanges', [...this.input().rateChanges, { from: '', rate: '' }]);
  }

  protected updateRateChange(index: number, field: 'from' | 'rate', value: string): void {
    this.set(
      'rateChanges',
      this.input().rateChanges.map((c, i) => (i === index ? { ...c, [field]: value } : c)),
    );
  }

  protected removeRateChange(index: number): void {
    this.set(
      'rateChanges',
      this.input().rateChanges.filter((_, i) => i !== index),
    );
  }

  protected addExtra(): void {
    this.set('extras', [...this.input().extras, { date: '', amount: '' }]);
  }

  protected updateExtra(index: number, field: 'date' | 'amount', value: string): void {
    this.set(
      'extras',
      this.input().extras.map((e, i) => (i === index ? { ...e, [field]: value } : e)),
    );
  }

  protected removeExtra(index: number): void {
    this.set(
      'extras',
      this.input().extras.filter((_, i) => i !== index),
    );
  }

  protected reset(): void {
    this.input.set(defaultInput(new Date()));
    this.result.set(undefined);
    this.error.set(undefined);
  }

  protected async calculate(event?: Event): Promise<void> {
    event?.preventDefault();
    const built = toRequest(this.input(), this.minorUnits());
    if ('error' in built) {
      this.error.set(built.error);
      return;
    }

    this.busy.set(true);
    const outcome = await this.calculator.calculate(built.request);
    this.busy.set(false);
    if (outcome.ok) {
      this.result.set(outcome.result);
      this.error.set(undefined);
    } else {
      this.error.set(outcome.error);
    }
  }

  protected monthYear(value: string): string {
    return new Date(`${value}T00:00:00Z`).toLocaleDateString('en-GB', {
      month: 'short',
      year: 'numeric',
      timeZone: 'UTC',
    });
  }

  protected date(value: string): string {
    return new Date(`${value}T00:00:00Z`).toLocaleDateString('nb-NO', { timeZone: 'UTC' });
  }

  protected percent(value: number): string {
    return `${value.toLocaleString('nb-NO', { maximumFractionDigits: 3 })} %`;
  }
}

function sum(
  rows: readonly ScheduleRow[],
  pick: (row: ScheduleRow) => number,
  minorUnits: number,
): number {
  // In whole minor units (øre, cents) so float sums of many rows don't drift.
  const scale = 10 ** minorUnits;
  return rows.reduce((total, row) => total + Math.round(pick(row) * scale), 0) / scale;
}

function loadInput(): LoanInput {
  const fallback = defaultInput(new Date());
  try {
    const saved = localStorage.getItem(storageKey);
    if (saved) {
      const parsed = JSON.parse(saved) as Partial<LoanInput>;
      return {
        ...fallback,
        ...parsed,
        rateChanges: Array.isArray(parsed.rateChanges) ? parsed.rateChanges : [],
        extras: Array.isArray(parsed.extras) ? parsed.extras : [],
      };
    }
  } catch {
    // Unreadable or blocked storage: start from the defaults.
  }
  return fallback;
}
