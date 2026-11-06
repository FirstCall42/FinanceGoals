import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { createAccount } from '../api/fn/finance-goals-api/create-account';
import { listAccounts } from '../api/fn/finance-goals-api/list-accounts';
import { updateAccount } from '../api/fn/finance-goals-api/update-account';
import { Account } from '../api/models/account';
import { AccountType } from '../api/models/account-type';
import { UpdateAccount } from '../api/models/update-account';

export type { Account, AccountType };

/** The account types, in the order the form offers them, with what to show for each. */
export const accountTypes: readonly { value: AccountType; label: string }[] = [
  { value: 'Current', label: 'Current account' },
  { value: 'Savings', label: 'Savings account' },
  { value: 'Deposit', label: 'Deposit' },
  { value: 'Brokerage', label: 'Brokerage / investments' },
  { value: 'Pension', label: 'Pension / retirement' },
  { value: 'Property', label: 'Property' },
];

/** The household's accounts (#35), through the generated API client. */
@Injectable({ providedIn: 'root' })
export class AccountService {
  private readonly api = inject(Api);

  list(profileId: number, includeArchived: boolean): Promise<Account[]> {
    return this.api.invoke(listAccounts, { profileId, includeArchived });
  }

  create(
    profileId: number,
    name: string,
    type: AccountType,
    currencyCode: string,
  ): Promise<Account> {
    return this.api.invoke(createAccount, { profileId, body: { name, type, currencyCode } });
  }

  update(profileId: number, accountId: number, changes: UpdateAccount): Promise<Account> {
    return this.api.invoke(updateAccount, { profileId, accountId, body: changes });
  }
}
