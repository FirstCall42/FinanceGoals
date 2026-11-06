import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LoanCalculator } from './loan-calculator';
import { LoanCalculationResult, LoanCalculatorService } from './loan-calculator.service';
import { LoanInput, defaultInput, formatMonths, parseNumber, toRequest } from './loan-input';

const nbsp = String.fromCharCode(0xa0);

const input = (changes: Partial<LoanInput> = {}): LoanInput => ({
  ...defaultInput(new Date(2026, 0, 20)),
  ...changes,
});

const result: LoanCalculationResult = {
  currency: 'NOK',
  withoutExtra: {
    lastPayment: '2056-01-20',
    payments: 360,
    totalInterest: 3_000_000,
    totalExtra: 0,
  },
  withExtra: {
    lastPayment: '2045-11-20',
    payments: 238,
    totalInterest: 1_800_000,
    totalExtra: 500_000,
  },
  monthsSaved: 122,
  interestSaved: 1_200_000,
  withoutExtraSchedule: [],
  withExtraSchedule: [
    {
      number: 1,
      date: '2026-02-20',
      annualRatePercent: 5.24,
      openingBalance: 3_000_000,
      interest: 13_100,
      principal: 3_447.89,
      payment: 16_547.89,
      extra: 5_000,
      fee: 85,
      balance: 2_991_552.11,
    },
  ],
};

describe('Loan calculator input', () => {
  it('reads numbers typed the Norwegian or English way', () => {
    expect(parseNumber('4 517 567')).toBe(4_517_567);
    expect(parseNumber('5,24')).toBe(5.24);
    expect(parseNumber('5.24')).toBe(5.24);
    expect(parseNumber(`4${nbsp}517${nbsp}567,50`)).toBe(4_517_567.5);
    expect(parseNumber('1,000.50')).toBeNaN();
    expect(parseNumber('abc')).toBeNaN();
    expect(parseNumber('')).toBeNaN();
  });

  it('builds the request with months and percent rates', () => {
    const built = toRequest(
      input({
        principal: '5 150 000',
        startRate: '6,04',
        rateChanges: [{ from: '2025-04-01', rate: '5,65' }],
        extras: [{ date: '2026-06-01', amount: '100 000' }],
        monthlyAmount: '5 000',
        monthlyFrom: '2026-12-01',
      }),
      2,
    );

    expect('request' in built && built.request).toEqual({
      currency: 'NOK',
      minorUnits: 2,
      principal: 5_150_000,
      paidOut: '2026-01-20',
      firstPayment: '2026-02-20',
      termMonths: 360,
      startRatePercent: 6.04,
      feePerPayment: 0,
      rateChanges: [{ from: '2025-04-01', annualRatePercent: 5.65 }],
      extras: [{ date: '2026-06-01', amount: 100_000 }],
      monthlyExtras: [{ amount: 5_000, from: '2026-12-01', until: null }],
    });
  });

  it('names the first thing to fix', () => {
    expect(toRequest(input({ principal: '' }), 2)).toEqual({
      error: 'Enter the loan amount, for example 3 000 000.',
    });
    expect(toRequest(input({ extras: [{ date: '', amount: '1000' }] }), 2)).toEqual({
      error: 'Every extra payment needs a date and an amount.',
    });
    expect(toRequest(input({ monthlyAmount: '5000' }), 2)).toEqual({
      error: 'The monthly extra needs an amount and a start date.',
    });
  });

  it('writes months as years and months', () => {
    expect(formatMonths(122)).toBe('10 years 2 months');
    expect(formatMonths(12)).toBe('1 year');
    expect(formatMonths(1)).toBe('1 month');
    expect(formatMonths(0)).toBe('0 months');
  });
});

describe('Loan calculator', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('shows the API message when the loan is refused', async () => {
    const built = toRequest(input(), 2);
    if (!('request' in built)) {
      throw new Error(built.error);
    }
    const outcome = TestBed.inject(LoanCalculatorService).calculate(built.request);
    http
      .expectOne('/api/loans/calculate')
      .flush(
        { errors: { loan: ['The term must be 1 to 600 months.'] } },
        { status: 400, statusText: 'Bad Request' },
      );

    expect(await outcome).toEqual({ ok: false, error: 'The term must be 1 to 600 months.' });
  });

  it('shows the payoff date, the time and interest saved, and the schedule', async () => {
    const fixture = TestBed.createComponent(LoanCalculator);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    const form = fixture.nativeElement.querySelector('form') as HTMLFormElement;

    form.dispatchEvent(new Event('submit'));
    const request = http.expectOne('/api/loans/calculate');
    expect(request.request.body.termMonths).toBe(360);
    request.flush(result);
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Nov 2045');
    expect(text).toContain('without extras: Jan 2056');
    expect(text).toContain('10 years 2 months');
    expect(text).toContain(`1${nbsp}200${nbsp}000,00${nbsp}NOK`);
    expect(text).toContain('2026');
    expect(localStorage.getItem('financegoals.loanCalculator')).toContain('3 000 000');
  });
});
