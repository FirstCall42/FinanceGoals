import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { deleteAccountAssumption } from '../api/fn/finance-goals-api/delete-account-assumption';
import { listAccountAssumptions } from '../api/fn/finance-goals-api/list-account-assumptions';
import { saveAccountAssumption } from '../api/fn/finance-goals-api/save-account-assumption';

/** What the household expects of an account: a yearly return in percent and a monthly contribution. */
export interface Assumption {
  readonly accountId: number;
  readonly returnPercent: number | null;
  readonly monthlyContribution: number | null;
}

const toNumber = (value: number | string | null | undefined): number | null =>
  value === null || value === undefined ? null : Number(value);

/** The expected return and monthly contribution of accounts (#152), through the generated API client. */
@Injectable({ providedIn: 'root' })
export class AssumptionService {
  private readonly api = inject(Api);

  async list(profileId: number): Promise<Assumption[]> {
    const all = await this.api.invoke(listAccountAssumptions, { profileId });
    return all.map((a) => ({
      accountId: Number(a.accountId),
      returnPercent: toNumber(a.expectedYearlyReturnPercent),
      monthlyContribution: toNumber(a.monthlyContribution),
    }));
  }

  async save(
    profileId: number,
    accountId: number,
    returnPercent: number | null,
    monthlyContribution: number | null,
  ): Promise<void> {
    await this.api.invoke(saveAccountAssumption, {
      profileId,
      accountId,
      body: { expectedYearlyReturnPercent: returnPercent, monthlyContribution },
    });
  }

  async clear(profileId: number, accountId: number): Promise<void> {
    await this.api.invoke(deleteAccountAssumption, { profileId, accountId });
  }
}
