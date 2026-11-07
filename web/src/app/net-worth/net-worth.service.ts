import { Injectable, inject } from '@angular/core';
import { Api } from '../api/api';
import { getBalanceForecast } from '../api/fn/finance-goals-api/get-balance-forecast';
import { getNetWorth } from '../api/fn/finance-goals-api/get-net-worth';
import { getNetWorthHistory } from '../api/fn/finance-goals-api/get-net-worth-history';
import { AccountType } from '../api/models/account-type';

/** One account's part of the net worth. */
export interface NetWorthLine {
  readonly accountId: number;
  readonly name: string;
  readonly type: AccountType;
  readonly currencyCode: string;
  /** The balance in the account's own currency. */
  readonly amount: number;
  /** The day of that balance: the latest on or before the date. */
  readonly balanceDate: string;
  /** In the shown currency; null when an exchange rate is missing. */
  readonly converted: number | null;
  /** The date of the oldest exchange rate used. */
  readonly rateDate: string | null;
  readonly liquid: boolean;
}

export interface NetWorth {
  readonly date: string;
  readonly currency: string;
  readonly total: number;
  readonly liquid: number;
  /** Currencies without an exchange rate; their accounts are left out of the totals. */
  readonly missingRates: readonly string[];
  readonly lines: readonly NetWorthLine[];
}

export interface NetWorthPoint {
  readonly date: string;
  readonly total: number;
  readonly liquid: number;
  readonly missingRates: readonly string[];
  /** The date of the oldest exchange rate behind this point; null when nothing was converted. */
  readonly rateDate: string | null;
}

/** One month of the forecast: the total with a low, an expected and a high return. */
export interface ForecastPoint {
  readonly date: string;
  readonly low: number;
  readonly expected: number;
  readonly high: number;
}

/** The balance forecast (#143): all accounts from their latest balances, at today's exchange rates. */
export interface Forecast {
  readonly currency: string;
  readonly from: string;
  /** How far the low and high lines sit from the expected return, in percentage points. */
  readonly spreadPoints: number;
  /** Currencies without an exchange rate; their accounts are left out. */
  readonly missingRates: readonly string[];
  /** True when an extreme input hit the cap the API stops amounts at. */
  readonly capped: boolean;
  readonly points: readonly ForecastPoint[];
}

/** Net worth and its monthly history (#37), through the generated API client. */
@Injectable({ providedIn: 'root' })
export class NetWorthService {
  private readonly api = inject(Api);

  async on(profileId: number, currency: string, date: string): Promise<NetWorth> {
    const result = await this.api.invoke(getNetWorth, { profileId, currency, date });
    // The API document allows numbers as text too, so convert once here.
    return {
      date: result.date,
      currency: result.currency,
      total: Number(result.total),
      liquid: Number(result.liquid),
      missingRates: result.missingRates,
      lines: result.lines.map((l) => ({
        accountId: Number(l.accountId),
        name: l.name,
        type: l.type,
        currencyCode: l.currencyCode,
        amount: Number(l.amount),
        balanceDate: l.balanceDate,
        converted: l.converted === null ? null : Number(l.converted),
        rateDate: l.rateDate,
        liquid: l.liquid,
      })),
    };
  }

  async history(profileId: number, currency: string, to: string): Promise<NetWorthPoint[]> {
    const points = await this.api.invoke(getNetWorthHistory, { profileId, currency, to });
    return points.map((p) => ({
      date: p.date,
      total: Number(p.total),
      liquid: Number(p.liquid),
      missingRates: p.missingRates,
      rateDate: p.rateDate,
    }));
  }

  async forecast(profileId: number, currency: string, months: number): Promise<Forecast> {
    const result = await this.api.invoke(getBalanceForecast, { profileId, currency, months });
    return {
      currency: result.currency,
      from: result.from,
      spreadPoints: Number(result.returnSpread) * 100,
      missingRates: result.missingRates,
      capped: result.capped,
      points: result.points.map((p) => ({
        date: p.date,
        low: Number(p.low),
        expected: Number(p.expected),
        high: Number(p.high),
      })),
    };
  }
}
