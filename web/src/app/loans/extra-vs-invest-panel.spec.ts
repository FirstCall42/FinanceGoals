import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ExtraVsInvest } from './extra-vs-invest-panel';
import { LoanCalculationResult, ScheduleRow } from './loan-calculator.service';
import { defaultInput } from './loan-input';

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

const result: LoanCalculationResult = {
  currency: 'NOK',
  withoutExtra: { lastPayment: '2030-03-01', payments: 3, totalInterest: 300, totalExtra: 0 },
  withExtra: { lastPayment: '2030-02-01', payments: 2, totalInterest: 200, totalExtra: 2000 },
  monthsSaved: 1,
  interestSaved: 100,
  withoutExtraSchedule: [row(1, 2000, 0), row(2, 1000, 0), row(3, 0, 0)],
  withExtraSchedule: [row(1, 1000, 1000), row(2, 0, 1000)],
};

describe('Extra payments or investing panel', () => {
  it('asks for the loan with only the monthly amount as extra, and shows the cases', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ExtraVsInvest);
    fixture.componentRef.setInput('loan', {
      ...defaultInput(new Date(2026, 0, 20)),
      extras: [{ date: '2026-06-01', amount: '1 000' }],
    });
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    const request = http.expectOne('/api/loans/calculate');
    expect(request.request.body.extras).toEqual([]);
    expect(request.request.body.monthlyExtras).toEqual([
      { amount: 5000, from: '2026-02-20', until: null },
    ]);
    request.flush(result);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(element.textContent).toContain('Low (4 %)');
    });

    // Changing the form's currency afterwards does not relabel the shown figures.
    fixture.componentRef.setInput('loan', {
      ...defaultInput(new Date(2026, 0, 20)),
      currency: 'USD',
    });
    fixture.detectChanges();
    expect(element.textContent).not.toContain('USD');
    expect(element.textContent).toContain('Expected (6 %)');
    expect(element.textContent).toContain('High (8 %)');
    expect(element.querySelectorAll('polyline').length).toBe(2);
    http.verify();
  });

  it('refuses a deduction above 100 %', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const fixture = TestBed.createComponent(ExtraVsInvest);
    fixture.componentRef.setInput('loan', defaultInput(new Date(2026, 0, 20)));
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    const deduction = element.querySelectorAll('input')[2];
    deduction.value = '150';
    deduction.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('[role="alert"]')?.textContent).toContain('between 0 and 100');
    http.expectNone('/api/loans/calculate');
  });
});
