import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { createAccount } from '../api/fn/finance-goals-api/create-account';
import { listAccounts } from '../api/fn/finance-goals-api/list-accounts';
import { updateAccount } from '../api/fn/finance-goals-api/update-account';
import { Account as ApiAccount } from '../api/models/account';
import { AccountType } from '../api/models/account-type';
import { UpdateAccount } from '../api/models/update-account';

export type { AccountType };

/** An account of the household. The API document allows numbers as text too; the id is a number here. */
export interface Account extends Omit<ApiAccount, 'id'> {
  readonly id: number;
}

/** The account types, in the order the form offers them, with what to show for each. */
export const accountTypes: readonly { value: AccountType; label: string }[] = [
  { value: 'Current', label: 'Current account' },
  { value: 'Savings', label: 'Savings account' },
  { value: 'Deposit', label: 'Deposit' },
  { value: 'Brokerage', label: 'Brokerage / investments' },
  { value: 'Pension', label: 'Pension / retirement' },
  { value: 'Property', label: 'Property' },
];

const normalize = (account: ApiAccount): Account => ({ ...account, id: Number(account.id) });

/** The household's accounts (#35), through the generated API client. */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly api = inject(Api);

  async list(profileId: number, includeArchived: boolean): Promise<Account[]> {
    return (await this.api.invoke(listAccounts, { profileId, includeArchived })).map(normalize);
  }

  async create(
    profileId: number,
    name: string,
    type: AccountType,
    currencyCode: string,
  ): Promise<Account> {
    return normalize(
      await this.api.invoke(createAccount, { profileId, body: { name, type, currencyCode } }),
    );
  }

  async update(profileId: number, accountId: number, changes: UpdateAccount): Promise<Account> {
    return normalize(await this.api.invoke(updateAccount, { profileId, accountId, body: changes }));
  }
}
