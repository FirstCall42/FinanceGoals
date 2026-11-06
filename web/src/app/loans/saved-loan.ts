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
