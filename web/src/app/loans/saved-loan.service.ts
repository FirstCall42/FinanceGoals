import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { addLoanMonthlyExtra } from '../api/fn/finance-goals-api/add-loan-monthly-extra';
import { addLoanOneOffExtra } from '../api/fn/finance-goals-api/add-loan-one-off-extra';
import { createLoan } from '../api/fn/finance-goals-api/create-loan';
import { getLoan } from '../api/fn/finance-goals-api/get-loan';
import { getLoanExtras } from '../api/fn/finance-goals-api/get-loan-extras';
import { listLoans } from '../api/fn/finance-goals-api/list-loans';
import { LoanInput } from './loan-input';
import { LoanCalculationRequest } from './loan-calculator.service';
import { inputFromStored, toCreateLoan } from './saved-loan';

export interface SavedLoan {
  readonly id: number;
  readonly name: string;
}

/** The household's stored loans (#139), through the generated API client. */
@Injectable({ providedIn: 'root' })
export class SavedLoanService {
  private readonly api = inject(Api);

  async list(profileId: number): Promise<SavedLoan[]> {
    const loans = await this.api.invoke(listLoans, { profileId });
    return loans.map((l) => ({ id: Number(l.id), name: l.name }));
  }

  /** The loan as the calculator form holds it, or why it can't be shown. */
  async open(profileId: number, loanId: number): Promise<LoanInput | string> {
    const [loan, extras] = await Promise.all([
      this.api.invoke(getLoan, { profileId, loanId }),
      this.api.invoke(getLoanExtras, { profileId, loanId }),
    ]);
    return inputFromStored(loan, extras);
  }

  /** Stores the loan, then its extra payments; the loan stays stored if an extra is refused. */
  async save(profileId: number, name: string, request: LoanCalculationRequest): Promise<SavedLoan> {
    const loan = await this.api.invoke(createLoan, {
      profileId,
      body: toCreateLoan(name, request),
    });
    const loanId = Number(loan.id);
    for (const e of request.extras) {
      await this.api.invoke(addLoanOneOffExtra, {
        profileId,
        loanId,
        body: { date: e.date, amount: e.amount },
      });
    }
    for (const m of request.monthlyExtras) {
      await this.api.invoke(addLoanMonthlyExtra, {
        profileId,
        loanId,
        body: { amount: m.amount, from: m.from, until: m.until },
      });
    }
    return { id: loanId, name: loan.name };
  }
}
