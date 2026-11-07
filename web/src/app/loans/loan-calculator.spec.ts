import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
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
        rateChanges: [{ from: '2026-04-01', rate: '5,65' }],
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
      rateChanges: [{ from: '2026-04-01', annualRatePercent: 5.65 }],
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

  const withExtras = (extras: { date: string; amount: string }[]) =>
    localStorage.setItem(
      'financegoals.loanCalculator',
      JSON.stringify({ ...defaultInput(new Date(2026, 0, 20)), extras }),
    );

  const openSaved = async () => {
    TestBed.inject(AuthService).profiles.set([
      { id: 4, name: 'Household', role: 'Owner', mainCurrency: 'NOK' },
    ]);
    const fixture = TestBed.createComponent(LoanCalculator);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    http.expectOne('/api/profiles/4/loans').flush([{ id: 9, name: 'Cabin' }]);
    const page = fixture.nativeElement as HTMLElement;
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Open Cabin');
    });
    const name = page.querySelector('#saved-title ~ label input') as HTMLInputElement;
    name.value = 'Home';
    name.dispatchEvent(new Event('input'));
    const save = Array.from(page.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Save this loan'),
    );
    return { fixture, page, save: () => save?.click() };
  };

  it('saves the loan, then its extra payments, and lists it', async () => {
    withExtras([{ date: '2026-06-15', amount: '100 000' }]);
    const { fixture, page, save } = await openSaved();

    save();
    const create = await vi.waitFor(() => http.expectOne('/api/profiles/4/loans'));
    expect(create.request.body.name).toBe('Home');
    create.flush({ id: 10, name: 'Home' });
    const extra = await vi.waitFor(() => http.expectOne('/api/profiles/4/loans/10/extras/one-off'));
    expect(extra.request.body).toEqual({ date: '2026-06-15', amount: 100000 });
    extra.flush({});

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Saved Home.');
      expect(page.textContent).toContain('Open Home');
    });
  });

  it('opens a saved loan from its latest recorded balance and says so', async () => {
    const { fixture, page } = await openSaved();
    const open = Array.from(page.querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Open Cabin'),
    );

    open?.click();
    const loan = await vi.waitFor(() => http.expectOne('/api/profiles/4/loans/9'));
    loan.flush({
      id: 9,
      name: 'Cabin',
      currencyCode: 'NOK',
      principal: 1_000_000,
      paidOut: '2026-01-01',
      firstPaymentDate: '2026-02-01',
      termMonths: 120,
      feePerPayment: 0,
      dayCount: 'ActualActual',
      periods: [
        {
          start: '2026-01-01',
          rateType: 'Variable',
          repaymentType: 'Annuity',
          rates: [{ effectiveFrom: '2026-01-01', annualRatePercent: 5 }],
        },
      ],
    });
    http.expectOne('/api/profiles/4/loans/9/extras').flush({
      currencyCode: 'NOK',
      oneOff: [],
      monthly: [],
      balances: [
        { date: '2026-03-10', amount: 990_000 },
        { date: '2026-09-15', amount: 940_000 },
      ],
    });

    // The latest balance starts the calculation: 940 000 left, eight payments made, 112 months to go.
    const calculate = await vi.waitFor(() => http.expectOne('/api/loans/calculate'));
    expect(calculate.request.body).toMatchObject({
      principal: 940_000,
      paidOut: '2026-09-15',
      firstPayment: '2026-10-01',
      termMonths: 112,
    });
    calculate.flush({ ...result, withoutExtraSchedule: [], withExtraSchedule: [] });
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Opened Cabin from the balance recorded 15.09.2026.');
    });
  });

  it('keeps a saved loan listed and says which extras failed', async () => {
    withExtras([{ date: '2026-06-15', amount: '100 000' }]);
    const { fixture, page, save } = await openSaved();

    save();
    const create = await vi.waitFor(() => http.expectOne('/api/profiles/4/loans'));
    create.flush({ id: 10, name: 'Home' });
    const extra = await vi.waitFor(() => http.expectOne('/api/profiles/4/loans/10/extras/one-off'));
    extra.flush(
      { errors: { extra: ['A one-off extra payment falls outside the loan.'] } },
      { status: 400, statusText: 'Bad Request' },
    );

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Saved Home, but not all extra payments');
      expect(page.textContent).toContain('falls outside the loan');
      expect(page.textContent).toContain('Open Home');
    });
  });

  it('does not save when two extra payments fall on the same day', async () => {
    withExtras([
      { date: '2026-06-15', amount: '1 000' },
      { date: '2026-06-15', amount: '2 000' },
    ]);
    const { fixture, page, save } = await openSaved();

    save();
    fixture.detectChanges();

    expect(page.textContent).toContain('same day');
    http.expectNone('/api/profiles/4/loans');
  });

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
