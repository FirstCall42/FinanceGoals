import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { deleteAccountValue } from '../api/fn/finance-goals-api/delete-account-value';
import { deleteSnapshot } from '../api/fn/finance-goals-api/delete-snapshot';
import { listAccountValues } from '../api/fn/finance-goals-api/list-account-values';
import { saveSnapshot } from '../api/fn/finance-goals-api/save-snapshot';

/** An account's balance on a date, in the account's own currency. */
export interface Balance {
  readonly accountId: number;
  /** yyyy-mm-dd */
  readonly date: string;
  readonly amount: number;
}

/** Account balances and snapshots (#36), through the generated API client. */
@Injectable({ providedIn: 'root' })
export class BalanceService {
  private readonly api = inject(Api);

  async list(profileId: number): Promise<Balance[]> {
    const values = await this.api.invoke(listAccountValues, { profileId });
    // The API document allows numbers as text too, so convert once here.
    return values.map((v) => ({
      accountId: Number(v.accountId),
      date: v.date,
      amount: Number(v.amount),
    }));
  }

  /** Records the balances of several accounts on one date; a balance already there is replaced. */
  saveSnapshot(
    profileId: number,
    date: string,
    values: readonly { accountId: number; amount: number }[],
  ): Promise<unknown> {
    return this.api.invoke(saveSnapshot, { profileId, body: { date, values: [...values] } });
  }

  deleteSnapshot(profileId: number, date: string): Promise<void> {
    return this.api.invoke(deleteSnapshot, { profileId, date });
  }

  deleteBalance(profileId: number, accountId: number, date: string): Promise<void> {
    return this.api.invoke(deleteAccountValue, { profileId, accountId, date });
  }
}
