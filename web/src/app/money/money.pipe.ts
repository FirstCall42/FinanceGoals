import { Pipe, PipeTransform } from '@angular/core';

/**
 * Writes an amount the one way the app shows money (#102; format choice: #103): Norwegian grouping
 * and decimals, the currency's minor units, a real minus sign, and the ISO code after the number,
 * e.g. "1 234 567,89 NOK". Spaces are non-breaking so an amount never wraps.
 *
 * Usage: {{ amount | money: 'NOK' }} or {{ amount | money: 'JPY' : 0 }}
 */
@Pipe({ name: 'money' })
export class MoneyPipe implements PipeTransform {
  transform(amount: number | null | undefined, currencyCode: string, minorUnits = 2): string {
    if (amount === null || amount === undefined || !Number.isFinite(amount)) {
      return '';
    }

    return formatMoney(amount, currencyCode, minorUnits);
  }
}

const nonBreakingSpace = String.fromCharCode(0xa0);

const formatters = new Map<number, Intl.NumberFormat>();

export function formatMoney(amount: number, currencyCode: string, minorUnits = 2): string {
  let formatter = formatters.get(minorUnits);
  if (!formatter) {
    formatter = new Intl.NumberFormat('nb-NO', {
      minimumFractionDigits: minorUnits,
      maximumFractionDigits: minorUnits,
    });
    formatters.set(minorUnits, formatter);
  }

  // nb-NO already uses a non-breaking space for grouping and U+2212 for the minus sign.
  return `${formatter.format(amount)}${nonBreakingSpace}${currencyCode}`;
}
