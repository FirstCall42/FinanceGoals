import { MoneyPipe, formatMoney } from './money.pipe';

const nbsp = ' ';
const minus = '−';

describe('formatMoney', () => {
  it('writes Norwegian grouping and decimals for a Norwegian browser', () => {
    expect(formatMoney(1234567.89, 'NOK', 2, 'nb-NO')).toBe(`1${nbsp}234${nbsp}567,89${nbsp}NOK`);
    expect(formatMoney(-2150000, 'NOK', 2, 'nb-NO')).toBe(
      `${minus}2${nbsp}150${nbsp}000,00${nbsp}NOK`,
    );
  });

  it('writes English grouping and decimals for an English browser', () => {
    expect(formatMoney(1234567.89, 'NOK', 2, 'en-US')).toBe(`1,234,567.89${nbsp}NOK`);
    expect(formatMoney(-2150000, 'NOK', 2, 'en-US')).toBe(`-2,150,000.00${nbsp}NOK`);
  });

  it('uses the currency minor units', () => {
    expect(formatMoney(125000, 'JPY', 0, 'nb-NO')).toBe(`125${nbsp}000${nbsp}JPY`);
    expect(formatMoney(1.2345, 'BHD', 3, 'en-US')).toBe(`1.235${nbsp}BHD`);
  });

  it('rounds to the minor units for display', () => {
    expect(formatMoney(3.125, 'NOK', 2, 'en-US')).toBe(`3.13${nbsp}NOK`);
  });
});

describe('MoneyPipe', () => {
  const pipe = new MoneyPipe();

  it('formats with the browser language and puts the code after the number', () => {
    expect(pipe.transform(1234.5, 'EUR')).toBe(formatMoney(1234.5, 'EUR', 2, navigator.language));
    expect(pipe.transform(1234.5, 'EUR').endsWith(`${nbsp}EUR`)).toBe(true);
  });

  it('writes nothing for a missing amount', () => {
    expect(pipe.transform(null, 'NOK')).toBe('');
    expect(pipe.transform(undefined, 'NOK')).toBe('');
    expect(pipe.transform(Number.NaN, 'NOK')).toBe('');
  });
});
