import { TestBed } from '@angular/core/testing';
import { PayoffChart } from './payoff-chart';
import { ScheduleRow } from './loan-calculator.service';

const row = (
  number: number,
  date: string,
  openingBalance: number,
  balance: number,
): ScheduleRow => ({
  number,
  date,
  annualRatePercent: 5,
  openingBalance,
  interest: 0,
  principal: openingBalance - balance,
  payment: openingBalance - balance,
  extra: 0,
  fee: 0,
  balance,
});

describe('Payoff chart', () => {
  const render = (standard: ScheduleRow[], withExtras: ScheduleRow[]) => {
    const fixture = TestBed.createComponent(PayoffChart);
    fixture.componentRef.setInput('standard', standard);
    fixture.componentRef.setInput('withExtras', withExtras);
    fixture.componentRef.setInput('currency', 'NOK');
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  };

  it('draws the standard and the extra-payment curve and says when each ends', () => {
    const page = render(
      [row(1, '2026-02-01', 1000, 500), row(2, '2026-03-01', 500, 0)],
      [row(1, '2026-02-01', 1000, 0)],
    );

    expect(page.querySelectorAll('polyline').length).toBe(2);
    const label = page.querySelector('svg')?.getAttribute('aria-label') ?? '';
    expect(label).toContain('paid off 03.2026 without extras, 02.2026 with extras');
    // The extra curve reaches the zero line before the standard one does.
    const last = (selector: string) =>
      (page.querySelector(selector)?.getAttribute('points') ?? '').split(' ').at(-1)?.split(',');
    expect(Number(last('.extra')?.[0])).toBeLessThan(Number(last('.standard')?.[0]));
    expect(last('.extra')?.[1]).toBe(last('.standard')?.[1]);
  });

  it('draws nothing without a schedule', () => {
    expect(render([], []).querySelector('svg')).toBeNull();
  });
});
