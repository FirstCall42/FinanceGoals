import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { addLoanMonthlyExtra } from '../api/fn/finance-goals-api/add-loan-monthly-extra';
import { addLoanOneOffExtra } from '../api/fn/finance-goals-api/add-loan-one-off-extra';
import { createLoan } from '../api/fn/finance-goals-api/create-loan';
import { getLoan } from '../api/fn/finance-goals-api/get-loan';
import { getLoanExtras } from '../api/fn/finance-goals-api/get-loan-extras';
import { saveLoanBalance } from '../api/fn/finance-goals-api/save-loan-balance';
import { setLoanSecuredOnProperty } from '../api/fn/finance-goals-api/set-loan-secured-on-property';
import { listLoans } from '../api/fn/finance-goals-api/list-loans';
import { messageOf } from '../profile/profile.service';
import { LoanInput } from './loan-input';
import { LoanCalculationRequest } from './loan-calculator.service';
import { inputFromBalance, inputFromStored, toCreateLoan } from './saved-loan';

export interface SavedLoan {
  readonly id: number;
  readonly name: string;
  readonly currencyCode: string;
  /** Secured on the home or other property: kept out of liquid net worth (#135). */
  readonly securedOnProperty: boolean;
}

/** The household's stored loans (#139), through the generated API client. */
@Injectable({ providedIn: 'root' })
export class SavedLoanService {
  private readonly api = inject(Api);

  async list(profileId: number): Promise<SavedLoan[]> {
    const loans = await this.api.invoke(listLoans, { profileId });
    return loans.map((l) => ({
      id: Number(l.id),
      name: l.name,
      currencyCode: l.currencyCode,
      securedOnProperty: l.securedOnProperty,
    }));
  }

  async setSecured(profileId: number, loanId: number, secured: boolean): Promise<SavedLoan> {
    const l = await this.api.invoke(setLoanSecuredOnProperty, {
      profileId,
      loanId,
      body: { securedOnProperty: secured },
    });
    return {
      id: Number(l.id),
      name: l.name,
      currencyCode: l.currencyCode,
      securedOnProperty: l.securedOnProperty,
    };
  }

  /** The balance the bank reports on a day: net worth counts the loan as a debt from then on. */
  async recordBalance(
    profileId: number,
    loanId: number,
    date: string,
    amount: number,
  ): Promise<void> {
    await this.api.invoke(saveLoanBalance, { profileId, loanId, date, body: { amount } });
  }

  /**
   * The loan as the calculator form holds it, or why it can't be shown. With a balance recorded from
   * the bank, it starts from the latest one (its date is returned), as that is where the loan really is.
   */
  async open(
    profileId: number,
    loanId: number,
  ): Promise<{ input: LoanInput; balanceDate: string | undefined } | string> {
    const [loan, extras] = await Promise.all([
      this.api.invoke(getLoan, { profileId, loanId }),
      this.api.invoke(getLoanExtras, { profileId, loanId }),
    ]);
    const stored = inputFromStored(loan, extras);
    const latest = [...extras.balances].sort((a, b) => b.date.localeCompare(a.date))[0];
    if (typeof stored === 'string' || !latest) {
      return typeof stored === 'string' ? stored : { input: stored, balanceDate: undefined };
    }

    const input = inputFromBalance(stored, Number(loan.termMonths), {
      date: latest.date,
      amount: Number(latest.amount),
    });
    return typeof input === 'string' ? input : { input, balanceDate: latest.date };
  }

  /**
   * Stores the loan, then its extra payments. The loan stays stored if an extra is refused, so the
   * result says which extras failed; the caller shows the loan as saved with that message.
   */
  async save(
    profileId: number,
    name: string,
    request: LoanCalculationRequest,
  ): Promise<{ loan: SavedLoan; failed: string | undefined }> {
    const loan = await this.api.invoke(createLoan, {
      profileId,
      body: toCreateLoan(name, request),
    });
    const loanId = Number(loan.id);
    let failed: string | undefined;
    for (const e of request.extras) {
      failed ??= await this.tryAdd(() =>
        this.api.invoke(addLoanOneOffExtra, {
          profileId,
          loanId,
          body: { date: e.date, amount: e.amount },
        }),
      );
    }
    for (const m of request.monthlyExtras) {
      failed ??= await this.tryAdd(() =>
        this.api.invoke(addLoanMonthlyExtra, {
          profileId,
          loanId,
          body: { amount: m.amount, from: m.from, until: m.until },
        }),
      );
    }
    return {
      loan: {
        id: loanId,
        name: loan.name,
        currencyCode: loan.currencyCode,
        securedOnProperty: loan.securedOnProperty,
      },
      failed,
    };
  }

  private async tryAdd(add: () => Promise<unknown>): Promise<string | undefined> {
    try {
      await add();
      return undefined;
    } catch (error) {
      return messageOf(error);
    }
  }
}
