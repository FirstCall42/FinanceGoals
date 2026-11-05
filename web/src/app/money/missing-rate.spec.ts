import { TestBed } from '@angular/core/testing';
import { MissingRate } from './missing-rate';

describe('MissingRate', () => {
  it('names every currency without a rate and shows no amount', async () => {
    const fixture = TestBed.createComponent(MissingRate);
    fixture.componentRef.setInput('currencyCodes', ['NOK', 'RON']);
    fixture.componentRef.setInput('date', '2026-10-31');
    await fixture.whenStable();

    const status = (fixture.nativeElement as HTMLElement).querySelector('[role="status"]');
    expect(status?.textContent?.trim()).toBe(
      "No exchange rate for NOK and RON on or before 2026-10-31, so this amount can't be shown.",
    );
  });
});
