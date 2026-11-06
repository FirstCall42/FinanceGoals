import { LoanExtrasView } from '../api/models/loan-extras-view';
import { LoanView } from '../api/models/loan-view';
import { LoanCalculationRequest } from './loan-calculator.service';
import { toRequest } from './loan-input';
import { inputFromStored, toCreateLoan } from './saved-loan';

const loan = (over: Partial<LoanView> = {}): LoanView => ({
  id: 1,
  name: 'Home',
  currencyCode: 'NOK',
  principal: 3_000_000,
  paidOut: '2026-01-01',
  firstPaymentDate: '2026-02-01',
  termMonths: 360,
  feePerPayment: 55,
  dayCount: 'ActualActual',
  periods: [
    {
      start: '2026-01-01',
      rateType: 'Variable',
      repaymentType: 'Annuity',
      rates: [
        { effectiveFrom: '2026-01-01', annualRatePercent: 5.24 },
        { effectiveFrom: '2026-07-01', annualRatePercent: 5.5 },
      ],
    },
  ],
  ...over,
});

const extras = (over: Partial<LoanExtrasView> = {}): LoanExtrasView => ({
  currencyCode: 'NOK',
  oneOff: [{ date: '2026-06-15', amount: 100_000 }],
  monthly: [{ id: 3, amount: 2_500, from: '2026-03-01', until: null }],
  balances: [],
  ...over,
});

describe('Saved loans in the calculator', () => {
  it('fills the form from a stored loan so it calculates the same request', () => {
    const input = inputFromStored(loan(), extras());
    expect(typeof input).toBe('object');
    if (typeof input === 'string') {
      return;
    }

    expect(input.startRate).toBe('5,24');
    expect(input.termYears).toBe('30');
    expect(input.rateChanges).toEqual([{ from: '2026-07-01', rate: '5,5' }]);
    const built = toRequest(input, 2);
    expect('request' in built && built.request).toMatchObject({
      termMonths: 360,
      startRatePercent: 5.24,
      feePerPayment: 55,
      rateChanges: [{ from: '2026-07-01', annualRatePercent: 5.5 }],
      extras: [{ date: '2026-06-15', amount: 100_000 }],
      monthlyExtras: [{ amount: 2_500, from: '2026-03-01', until: null }],
    });
  });

  it('says why a loan with fixed, interest-only or several periods does not open', () => {
    const fixed = loan({
      periods: [{ start: '2026-01-01', rateType: 'Fixed', repaymentType: 'Annuity', rates: [] }],
    });
    expect(inputFromStored(fixed, extras())).toContain('cannot show it yet');
    expect(
      inputFromStored(
        loan(),
        extras({
          monthly: [
            { id: 1, amount: 1, from: '2026-03-01', until: null },
            { id: 2, amount: 2, from: '2026-04-01', until: null },
          ],
        }),
      ),
    ).toContain('several monthly');
  });

  it('turns what the calculator was asked into one variable-rate annuity period', () => {
    const request: LoanCalculationRequest = {
      currency: 'NOK',
      minorUnits: 2,
      principal: 3_000_000,
      paidOut: '2026-01-01',
      firstPayment: '2026-02-01',
      termMonths: 360,
      startRatePercent: 5.24,
      feePerPayment: 0,
      rateChanges: [{ from: '2026-07-01', annualRatePercent: 5.5 }],
      extras: [],
      monthlyExtras: [],
    };

    const body = toCreateLoan('Home', request);

    expect(body.name).toBe('Home');
    expect(body.periods).toHaveLength(1);
    expect(body.periods?.[0]).toMatchObject({ rateType: 'Variable', repaymentType: 'Annuity' });
    expect(body.periods?.[0].rates).toEqual([
      { effectiveFrom: '2026-01-01', annualRatePercent: 5.24 },
      { effectiveFrom: '2026-07-01', annualRatePercent: 5.5 },
    ]);
  });
});
