import { Pipe, PipeTransform } from '@angular/core';

/**
 * Writes an amount the one way the app shows money: grouping and decimals follow the browser's
 * language (owner decision, #103), with the currency's minor units and the ISO code after the
 * number, e.g. "1 234 567,89 NOK" in Norwegian or "1,234,567.89 NOK" in English. The space before
 * the code is non-breaking so an amount never wraps.
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

const formatters = new Map<string, Intl.NumberFormat>();

/** The browser's preferred language, e.g. "nb-NO"; undefined lets Intl use its default. */
function browserLocale(): string | undefined {
  return typeof navigator === 'undefined' ? undefined : navigator.language || undefined;
}

export function formatMoney(
  amount: number,
  currencyCode: string,
  minorUnits = 2,
  locale: string | undefined = browserLocale(),
): string {
  const key = `${locale ?? ''}|${minorUnits}`;
  let formatter = formatters.get(key);
  if (!formatter) {
    formatter = new Intl.NumberFormat(locale, {
      minimumFractionDigits: minorUnits,
      maximumFractionDigits: minorUnits,
    });
    formatters.set(key, formatter);
  }

  return `${formatter.format(amount)}${nonBreakingSpace}${currencyCode}`;
}
