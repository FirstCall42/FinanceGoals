import { ScheduleRow } from './loan-calculator.service';

/** The low and high returns sit this far (percentage points) from the expected one, as on the forecast. */
export const returnSpreadPoints = 2;

export interface ComparisonInput {
  /** The loan's standard schedule, and the one with the extra amount paid every month. */
  readonly standard: readonly ScheduleRow[];
  readonly withExtra: readonly ScheduleRow[];
  /** The monthly amount that goes to extra payments in one case and to the fund in the other. */
  readonly amount: number;
  /** The fund's expected yearly return, in percent. */
  readonly returnPercent: number;
  /** Share of the interest paid back through the tax deduction (rentefradrag), in percent. */
  readonly taxDeductionPercent: number;
  readonly horizonYears: number;
  readonly minorUnits: number;
}

/** Net worth from this loan and the fund after a month: the fund minus what is still owed. */
export interface ComparisonPoint {
  readonly month: number;
  readonly extraPayments: number;
  readonly invest: number;
}

export interface ReturnCase {
  readonly label: 'Low' | 'Expected' | 'High';
  readonly yearlyPercent: number;
  /** Extra payments minus investing, after 5, 10 and 20 years (when the horizon reaches them). */
  readonly after: readonly { readonly years: number; readonly difference: number }[];
  /** The same difference when the standard schedule would have been paid off. */
  readonly atPayoff: number;
}

export interface ComparisonResult {
  readonly points: readonly ComparisonPoint[];
  readonly cases: readonly ReturnCase[];
  readonly standardPayoff: string;
  readonly extraPayoff: string;
}

/**
 * Puts the same money every month into extra loan payments or into a fund. The cash spent is the
 * same in both: what the standard schedule asks plus the amount. When a loan is paid off, what it no
 * longer asks goes to the fund. Interest is partly paid back by the tax deduction, which also goes to
 * the fund in the month it is paid (a simplification: in reality it comes with the tax settlement).
 * The fund grows by the yearly return compounded monthly, rounded to the currency's minor units.
 */
export function compareExtraAndInvest(input: ComparisonInput): ComparisonResult {
  const { standard, withExtra, amount, minorUnits } = input;
  const months = Math.max(Math.round(input.horizonYears * 12), standard.length);
  const outlay = (row: ScheduleRow | undefined) => (row ? row.payment + row.extra + row.fee : 0);
  // What the loan asks on a normal month, kept after it is paid off so the budget stays the same.
  const lastRegular = standard.length > 1 ? standard[standard.length - 2] : standard[0];
  const regular = outlay(lastRegular) - (lastRegular?.extra ?? 0);
  const tax = input.taxDeductionPercent / 100;

  const run = (rows: readonly ScheduleRow[], growth: number): number[] => {
    const net: number[] = [];
    let fund = 0;
    for (let month = 1; month <= months; month++) {
      const row = rows[month - 1];
      const budget = (standard[month - 1] ? outlay(standard[month - 1]) : regular) + amount;
      const deduction = (row?.interest ?? 0) * tax;
      fund = round(fund * growth + (budget - outlay(row)) + deduction, minorUnits);
      net.push(round(fund - (row?.balance ?? 0), minorUnits));
    }
    return net;
  };

  const monthly = (percent: number) => Math.pow(1 + Math.max(percent, -99) / 100, 1 / 12);
  const cases = (
    [
      ['Low', input.returnPercent - returnSpreadPoints],
      ['Expected', input.returnPercent],
      ['High', input.returnPercent + returnSpreadPoints],
    ] as const
  ).map(([label, percent]): ReturnCase => {
    const a = run(withExtra, monthly(percent));
    const b = run(standard, monthly(percent));
    const at = (month: number) => round(a[month - 1] - b[month - 1], minorUnits);
    return {
      label,
      yearlyPercent: percent,
      after: [5, 10, 20]
        .filter((years) => years * 12 <= Math.round(input.horizonYears * 12))
        .map((years) => ({ years, difference: at(years * 12) })),
      atPayoff: at(standard.length),
    };
  });

  const a = run(withExtra, monthly(input.returnPercent));
  const b = run(standard, monthly(input.returnPercent));
  const shown = Math.round(input.horizonYears * 12);
  return {
    points: a
      .slice(0, shown)
      .map((value, i) => ({ month: i + 1, extraPayments: value, invest: b[i] })),
    cases,
    standardPayoff: standard[standard.length - 1].date,
    extraPayoff: withExtra[withExtra.length - 1].date,
  };
}

function round(value: number, minorUnits: number): number {
  const factor = Math.pow(10, minorUnits);
  return Math.round(value * factor) / factor;
}
