import { LoanExtrasView } from '../api/models/loan-extras-view';
import { LoanView } from '../api/models/loan-view';
import { LoanCalculationRequest } from './loan-calculator.service';
import { toRequest } from './loan-input';
import { inputFromBalance, inputFromStored, toCreateLoan } from './saved-loan';

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

  it('refuses a loan that counts interest days the 30/360 way', () => {
    expect(inputFromStored(loan({ dayCount: 'Thirty360' }), extras())).toContain('interest days');
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

  describe('from the latest recorded balance', () => {
    const stored = () => {
      const input = inputFromStored(loan(), extras());
      if (typeof input === 'string') {
        throw new Error(input);
      }
      return input;
    };

    it('starts the loan at the balance with the payments, term and rate that are left', () => {
      const input = inputFromBalance(stored(), 360, { date: '2026-09-15', amount: 2_900_000 });
      if (typeof input === 'string') {
        throw new Error(input);
      }

      // Eight payments (February to September) are made: the next is 1 October, 352 months are left.
      expect(input.principal).toBe('2900000');
      expect(input.paidOut).toBe('2026-09-15');
      expect(input.firstPayment).toBe('2026-10-01');
      const built = toRequest(input, 2);
      expect('request' in built && built.request.termMonths).toBe(352);
      // The rate in force on the day continues; extras before the day are history.
      expect(input.startRate).toBe('5,5');
      expect(input.rateChanges).toEqual([]);
      expect(input.extras).toEqual([]);
      expect(input.monthlyAmount).toBe('2500');
    });

    it('keeps rate changes and extra payments that come after the balance', () => {
      const input = inputFromBalance(stored(), 360, { date: '2026-05-10', amount: 2_950_000 });
      if (typeof input === 'string') {
        throw new Error(input);
      }

      expect(input.startRate).toBe('5,24');
      expect(input.rateChanges).toEqual([{ from: '2026-07-01', rate: '5,5' }]);
      expect(input.extras).toEqual([{ date: '2026-06-15', amount: '100000' }]);
    });

    it('drops a monthly extra that ended before the balance', () => {
      const ended = { ...stored(), monthlyUntil: '2026-05-01' };

      const input = inputFromBalance(ended, 360, { date: '2026-09-15', amount: 2_900_000 });

      expect(typeof input === 'object' && input.monthlyAmount).toBe('');
    });

    it('keeps the payment day at the end of a short month', () => {
      const monthEnd = { ...stored(), firstPayment: '2026-01-31' };

      const input = inputFromBalance(monthEnd, 360, { date: '2026-03-10', amount: 2_990_000 });

      // January 31 and February 28 are paid; the next is March 31.
      expect(typeof input === 'object' && input.firstPayment).toBe('2026-03-31');
    });

    it('says so when nothing is left to pay', () => {
      const input = inputFromBalance(stored(), 360, { date: '2056-03-01', amount: 100 });

      expect(input).toContain('no payments left');
    });
  });
});
