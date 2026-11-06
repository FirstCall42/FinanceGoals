import { LoanCalculationRequest } from './loan-calculator.service';

/** What the calculator form holds: text as typed, so "5,24" and "4 517 567" both work. */
export interface LoanInput {
  currency: string;
  principal: string;
  paidOut: string;
  firstPayment: string;
  termYears: string;
  startRate: string;
  fee: string;
  rateChanges: { from: string; rate: string }[];
  extras: { date: string; amount: string }[];
  monthlyAmount: string;
  monthlyFrom: string;
  monthlyUntil: string;
}

// Normal, non-breaking and narrow non-breaking spaces, as copied from a bank statement.
const spaces = new RegExp(`[\\s${String.fromCharCode(0xa0, 0x202f)}]`, 'g');

/** A number typed the Norwegian or the English way: spaces between thousands, comma or point. */
export function parseNumber(text: string): number {
  const cleaned = text.replace(spaces, '').replace(',', '.');
  return cleaned === '' || !/^-?\d*\.?\d*$/.test(cleaned) ? Number.NaN : Number(cleaned);
}

/** A starting point with round figures; the person replaces them with their own loan. */
export function defaultInput(today: Date): LoanInput {
  // Day 28 at the latest, so the next month has the same day and the first period is a whole month.
  const day = Math.min(today.getDate(), 28);
  const paidOut = isoDate(new Date(today.getFullYear(), today.getMonth(), day));
  const first = new Date(Date.UTC(today.getFullYear(), today.getMonth() + 1, day));
  return {
    currency: 'NOK',
    principal: '3 000 000',
    paidOut,
    firstPayment: first.toISOString().slice(0, 10),
    termYears: '30',
    startRate: '5',
    fee: '0',
    rateChanges: [],
    extras: [],
    monthlyAmount: '',
    monthlyFrom: '',
    monthlyUntil: '',
  };
}

/** The request for the API, or the first thing to fix in the form. */
export function toRequest(
  input: LoanInput,
  minorUnits: number,
): { request: LoanCalculationRequest } | { error: string } {
  const principal = parseNumber(input.principal);
  const years = parseNumber(input.termYears);
  const startRate = parseNumber(input.startRate);
  const fee = input.fee.trim() === '' ? 0 : parseNumber(input.fee);
  if (!(principal > 0)) {
    return { error: 'Enter the loan amount, for example 3 000 000.' };
  }

  if (!(years > 0)) {
    return { error: 'Enter the term in years, for example 30.' };
  }

  if (!(startRate >= 0)) {
    return { error: 'Enter the starting rate in percent, for example 5,24.' };
  }

  if (!(fee >= 0)) {
    return { error: 'Enter the fee per payment as a number, or 0.' };
  }

  if (!input.paidOut || !input.firstPayment) {
    return { error: 'Enter the payout date and the first payment date.' };
  }

  const rateChanges = [];
  for (const change of input.rateChanges) {
    const rate = parseNumber(change.rate);
    if (!change.from || !(rate >= 0)) {
      return { error: 'Every rate change needs a date and a rate in percent.' };
    }
    rateChanges.push({ from: change.from, annualRatePercent: rate });
  }

  const extras = [];
  for (const extra of input.extras) {
    const amount = parseNumber(extra.amount);
    if (!extra.date || !(amount > 0)) {
      return { error: 'Every extra payment needs a date and an amount.' };
    }
    extras.push({ date: extra.date, amount });
  }

  const monthlyExtras = [];
  if (input.monthlyAmount.trim() !== '') {
    const amount = parseNumber(input.monthlyAmount);
    if (!(amount > 0) || !input.monthlyFrom) {
      return { error: 'The monthly extra needs an amount and a start date.' };
    }
    monthlyExtras.push({ amount, from: input.monthlyFrom, until: input.monthlyUntil || null });
  }

  return {
    request: {
      currency: input.currency,
      minorUnits,
      principal,
      paidOut: input.paidOut,
      firstPayment: input.firstPayment,
      termMonths: Math.round(years * 12),
      startRatePercent: startRate,
      feePerPayment: fee,
      rateChanges,
      extras,
      monthlyExtras,
    },
  };
}

/** "10 years 2 months", "1 year", "5 months". */
export function formatMonths(months: number): string {
  const years = Math.floor(months / 12);
  const rest = months % 12;
  const parts = [];
  if (years > 0) {
    parts.push(`${years} ${years === 1 ? 'year' : 'years'}`);
  }
  if (rest > 0 || years === 0) {
    parts.push(`${rest} ${rest === 1 ? 'month' : 'months'}`);
  }
  return parts.join(' ');
}

/** A local date as yyyy-mm-dd. */
export function isoDate(date: Date): string {
  return new Date(Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()))
    .toISOString()
    .slice(0, 10);
}
