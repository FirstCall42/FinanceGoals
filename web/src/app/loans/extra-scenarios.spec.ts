import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ExtraScenarios, toScenarios } from './extra-scenarios';
import { LoanCalculationResult, ScheduleRow } from './loan-calculator.service';
import { defaultInput, isoDate } from './loan-input';

function row(number: number, balance: number, extra: number): ScheduleRow {
  return {
    number,
    date: `2030-0${number}-01`,
    annualRatePercent: 5,
    openingBalance: balance + 1000 + extra,
    interest: 100,
    principal: 900,
    payment: 1000,
    extra,
    fee: 0,
    balance,
  };
}

function result(extraMonths: number, interestSaved: number): LoanCalculationResult {
  return {
    currency: 'NOK',
    withoutExtra: { lastPayment: '2030-03-01', payments: 3, totalInterest: 300, totalExtra: 0 },
    withExtra: {
      lastPayment: '2030-02-01',
      payments: 2,
      totalInterest: 300 - interestSaved,
      totalExtra: 2000,
    },
    monthsSaved: extraMonths,
    interestSaved,
    withoutExtraSchedule: [row(1, 2000, 0), row(2, 1000, 0), row(3, 0, 0)],
    withExtraSchedule: [row(1, 1000, 1000), row(2, 0, 1000)],
  };
}

describe('Extra payment scenarios', () => {
  it('puts the loan without extras first, then one scenario per amount', () => {
    const scenarios = toScenarios([
      { amount: 10000, result: result(1, 100) },
      { amount: 18500, result: result(1, 100) },
    ]);
    expect(scenarios.map((s) => s.amount)).toEqual([0, 10000, 18500]);
    expect(scenarios[0].lastPayment).toBe('2030-03-01');
    expect(scenarios[1].lastPayment).toBe('2030-02-01');
  });

  it('calculates each amount as a monthly extra and draws one line per scenario', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ExtraScenarios);
    fixture.componentRef.setInput('loan', {
      ...defaultInput(new Date(2026, 0, 20)),
      extras: [{ date: '2026-06-01', amount: '1 000' }],
    });
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    const requests = http.match('/api/loans/calculate');
    expect(requests.map((r) => r.request.body.monthlyExtras)).toEqual([
      [{ amount: 10000, from: isoDate(new Date()), until: null }],
      [{ amount: 18500, from: isoDate(new Date()), until: null }],
    ]);
    expect(requests.every((r) => r.request.body.extras.length === 0)).toBe(true);
    requests.forEach((r) => r.flush(result(1, 100)));
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(element.querySelectorAll('tbody tr').length).toBe(3);
    });
    expect(element.querySelectorAll('polyline').length).toBe(3);
    http.verify();
  });

  it('refuses an amount that is not above zero', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ExtraScenarios);
    fixture.componentRef.setInput('loan', defaultInput(new Date(2026, 0, 20)));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const first = element.querySelector('input')!;
    first.value = '0';
    first.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain('above zero');
    http.expectNone('/api/loans/calculate');
  });
});
