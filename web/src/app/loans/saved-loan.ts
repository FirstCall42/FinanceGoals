import { CreateLoan } from '../api/models/create-loan';
import { LoanExtrasView } from '../api/models/loan-extras-view';
import { LoanView } from '../api/models/loan-view';
import { LoanInput } from './loan-input';
import { LoanCalculationRequest } from './loan-calculator.service';

/** A percent or an amount as the form shows it: Norwegian decimal comma. */
const typed = (value: number | string): string => String(value).replace('.', ',');

/**
 * A stored loan as the calculator form holds it, or the reason the calculator can't show it yet.
 * The calculator takes one variable-rate annuity period, any rate changes, one-off extras and one
 * monthly extra.
 */
export function inputFromStored(loan: LoanView, extras: LoanExtrasView): LoanInput | string {
  if (loan.dayCount !== 'ActualActual') {
    return 'This loan counts interest days differently from the calculator; it cannot show it yet.';
  }
  const [period, ...more] = loan.periods;
  if (more.length > 0 || period.rateType !== 'Variable' || period.repaymentType !== 'Annuity') {
    return 'This loan has fixed-rate, interest-only or several periods; the calculator cannot show it yet.';
  }
  if (extras.monthly.length > 1) {
    return 'This loan has several monthly extra payments; the calculator takes one.';
  }
  const [first, ...changes] = period.rates;
  const monthly = extras.monthly[0];
  return {
    currency: loan.currencyCode,
    principal: String(loan.principal),
    paidOut: loan.paidOut,
    firstPayment: loan.firstPaymentDate,
    termYears: typed(Math.round((Number(loan.termMonths) / 12) * 1e4) / 1e4),
    startRate: typed(first.annualRatePercent),
    fee: String(loan.feePerPayment),
    rateChanges: changes.map((c) => ({ from: c.effectiveFrom, rate: typed(c.annualRatePercent) })),
    extras: extras.oneOff.map((e) => ({ date: e.date, amount: String(e.amount) })),
    monthlyAmount: monthly ? String(monthly.amount) : '',
    monthlyFrom: monthly?.from ?? '',
    monthlyUntil: monthly?.until ?? '',
  };
}

/** The same day in another month, kept inside the month (31 January plus a month is 28 February). */
function addMonths(iso: string, months: number): string {
  const [y, m, d] = iso.split('-').map(Number);
  const first = new Date(Date.UTC(y, m - 1 + months, 1));
  const last = new Date(Date.UTC(first.getUTCFullYear(), first.getUTCMonth() + 1, 0)).getUTCDate();
  first.setUTCDate(Math.min(d, last));
  return first.toISOString().slice(0, 10);
}

/**
 * The loan as it stands on a day the bank reported its balance (#144): that balance is the new
 * principal, paid out that day, with the payments, the term and the rates that are left. Real balances
 * drift from the plan with extra payments, so this is where a forecast should start. The balance is
 * taken as after any payment made that day. Returns the reason when nothing is left to pay.
 */
export function inputFromBalance(
  loan: LoanInput,
  termMonths: number,
  balance: { readonly date: string; readonly amount: number },
): LoanInput | string {
  let made = 0;
  while (made < termMonths && addMonths(loan.firstPayment, made) <= balance.date) {
    made++;
  }
  if (made >= termMonths || balance.amount <= 0) {
    return 'This loan has no payments left after its latest recorded balance.';
  }

  const left = termMonths - made;
  const next = addMonths(loan.firstPayment, made);
  // A monthly extra that ends before the next payment has nothing left to pay (the API refuses one).
  const ended = loan.monthlyUntil !== '' && loan.monthlyUntil < next;
  // The rate in force on the balance day starts the new loan; later changes stay.
  const rates = [{ from: loan.paidOut, rate: loan.startRate }, ...loan.rateChanges];
  const inForce = rates.filter((c) => c.from <= balance.date).at(-1) ?? rates[0];
  return {
    ...loan,
    principal: String(balance.amount),
    paidOut: balance.date,
    firstPayment: next,
    termYears: typed(Math.round((left / 12) * 1e4) / 1e4),
    startRate: inForce.rate,
    rateChanges: rates.filter((c) => c.from > balance.date),
    extras: loan.extras.filter((e) => e.date > balance.date),
    monthlyFrom: ended ? '' : loan.monthlyFrom,
    monthlyAmount: ended ? '' : loan.monthlyAmount,
    monthlyUntil: ended ? '' : loan.monthlyUntil,
  };
}

/** The loan to store from what the calculator was asked: one variable-rate annuity period. */
export function toCreateLoan(name: string, request: LoanCalculationRequest): CreateLoan {
  return {
    name,
    currencyCode: request.currency,
    principal: request.principal,
    paidOut: request.paidOut,
    firstPaymentDate: request.firstPayment,
    termMonths: request.termMonths,
    feePerPayment: request.feePerPayment,
    dayCount: 'ActualActual',
    periods: [
      {
        start: request.paidOut,
        rateType: 'Variable',
        repaymentType: 'Annuity',
        rates: [
          { effectiveFrom: request.paidOut, annualRatePercent: request.startRatePercent },
          ...request.rateChanges.map((r) => ({
            effectiveFrom: r.from,
            annualRatePercent: r.annualRatePercent,
          })),
        ],
      },
    ],
  };
}
