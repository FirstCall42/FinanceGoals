import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../config/app-config';

/** POST /api/loans/calculate. Dates are ISO strings (yyyy-mm-dd); rates are percent (5.24). */
export interface LoanCalculationRequest {
  readonly currency: string;
  readonly minorUnits: number;
  readonly principal: number;
  readonly paidOut: string;
  readonly firstPayment: string;
  readonly termMonths: number;
  readonly startRatePercent: number;
  readonly feePerPayment: number;
  readonly rateChanges: readonly { from: string; annualRatePercent: number }[];
  readonly extras: readonly { date: string; amount: number }[];
  readonly monthlyExtras: readonly { amount: number; from: string; until: string | null }[];
}

export interface ScheduleRow {
  readonly number: number;
  readonly date: string;
  readonly annualRatePercent: number;
  readonly openingBalance: number;
  readonly interest: number;
  readonly principal: number;
  readonly payment: number;
  readonly extra: number;
  readonly fee: number;
  readonly balance: number;
}

export interface ScheduleSummary {
  readonly lastPayment: string;
  readonly payments: number;
  readonly totalInterest: number;
  readonly totalExtra: number;
}

export interface LoanCalculationResult {
  readonly currency: string;
  readonly withoutExtra: ScheduleSummary;
  readonly withExtra: ScheduleSummary;
  readonly monthsSaved: number;
  readonly interestSaved: number;
  readonly withoutExtraSchedule: readonly ScheduleRow[];
  readonly withExtraSchedule: readonly ScheduleRow[];
}

/** The calculation, or the message to show when the API refused the input or could not be reached. */
export type LoanCalculationOutcome =
  | { readonly ok: true; readonly result: LoanCalculationResult }
  | { readonly ok: false; readonly error: string };

/** Calculates a loan on the API; nothing is stored there. */
@Injectable({ providedIn: 'root' })
export class LoanCalculatorService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  async calculate(request: LoanCalculationRequest): Promise<LoanCalculationOutcome> {
    try {
      const result = await firstValueFrom(
        this.http.post<LoanCalculationResult>(this.config.apiUrl('/api/loans/calculate'), request),
      );
      return { ok: true, result };
    } catch (error) {
      return { ok: false, error: messageOf(error) };
    }
  }
}

function messageOf(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.status === 400) {
    const errors = (error.error as { errors?: Record<string, string[]> } | null)?.errors;
    const first = errors ? Object.values(errors).flat()[0] : undefined;
    if (first) {
      return first;
    }

    return 'Check the loan details: some of them are not valid.';
  }

  return "The calculation couldn't be done right now. Try again in a minute.";
}
