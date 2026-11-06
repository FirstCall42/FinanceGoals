import { ScheduleRow } from './loan-calculator.service';
import { compareExtraAndInvest } from './extra-vs-invest';

/** A loan paid in equal parts with a fixed interest amount per month, so figures can be checked by hand. */
function schedule(principal: number, payments: number, extra = 0, interest = 0): ScheduleRow[] {
  const rows: ScheduleRow[] = [];
  let balance = principal;
  for (let n = 1; balance > 0; n++) {
    const part = Math.min(principal / payments, balance);
    const more = Math.min(extra, balance - part);
    rows.push({
      number: n,
      date: `2030-${String(((n - 1) % 12) + 1).padStart(2, '0')}-01`,
      annualRatePercent: 0,
      openingBalance: balance,
      interest,
      principal: part,
      payment: part + interest,
      extra: more,
      fee: 0,
      balance: balance - part - more,
    });
    balance -= part + more;
  }
  return rows;
}

const base = {
  amount: 1000,
  returnPercent: 0,
  taxDeductionPercent: 0,
  horizonYears: 2,
  minorUnits: 2,
};

const expected = (r: ReturnType<typeof compareExtraAndInvest>) =>
  r.cases.find((c) => c.label === 'Expected')!;

describe('Extra payments or investing', () => {
  it('is a tie when the fund earns nothing and the loan costs nothing', () => {
    const result = compareExtraAndInvest({
      ...base,
      standard: schedule(12000, 12),
      withExtra: schedule(12000, 12, 1000),
    });

    expect(expected(result).atPayoff).toBe(0);
    expect(result.points.at(-1)?.extraPayments).toBe(result.points.at(-1)?.invest);
  });

  it('keeps the freed payment: after a loan is paid off, what it asked goes to the fund', () => {
    const result = compareExtraAndInvest({
      ...base,
      standard: schedule(12000, 12),
      withExtra: schedule(12000, 12, 1000),
    });

    // 24 months of 2 000 is 48 000 spent; 12 000 of it repaid the loan, the other 36 000 is the fund.
    expect(result.points.at(-1)?.invest).toBe(36000);
    expect(result.extraPayoff <= result.standardPayoff).toBe(true);
  });

  it('lets a higher return favour investing', () => {
    const standard = schedule(12000, 12, 0, 100);
    const withExtra = schedule(12000, 12, 1000, 50);
    const plain = compareExtraAndInvest({ ...base, standard, withExtra });
    const grown = compareExtraAndInvest({ ...base, standard, withExtra, returnPercent: 10 });

    expect(expected(grown).atPayoff).toBeLessThan(expected(plain).atPayoff);
  });

  it('credits the deduction on the interest of each case: only the interest saved is worth less', () => {
    // 1 200 of interest on the standard schedule (12 x 100), 300 with extra payments (6 x 50).
    const standard = schedule(12000, 12, 0, 100);
    const withExtra = schedule(12000, 12, 1000, 50);
    const plain = compareExtraAndInvest({ ...base, horizonYears: 1, standard, withExtra });
    const deducted = compareExtraAndInvest({
      ...base,
      horizonYears: 1,
      standard,
      withExtra,
      taxDeductionPercent: 22,
    });

    // 900 of interest saved; 22 % of the interest paid comes back in both cases: 900 - 0.22 x 900.
    expect(expected(plain).atPayoff).toBe(900);
    expect(expected(deducted).atPayoff).toBe(702);
  });

  it('shows the 5, 10 and 20 year columns only as far as the horizon reaches', () => {
    const result = compareExtraAndInvest({
      ...base,
      horizonYears: 5,
      standard: schedule(24000, 24),
      withExtra: schedule(24000, 24, 1000),
    });

    expect(expected(result).after.map((a) => a.years)).toEqual([5]);
  });

  it('gives a low, expected and high case two percentage points apart', () => {
    const result = compareExtraAndInvest({
      ...base,
      returnPercent: 6,
      standard: schedule(12000, 12),
      withExtra: schedule(12000, 12, 1000),
    });

    expect(result.cases.map((c) => c.yearlyPercent)).toEqual([4, 6, 8]);
    expect(result.cases[0].after).toEqual([]);
  });
});
