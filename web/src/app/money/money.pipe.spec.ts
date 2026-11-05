import { MoneyPipe, formatMoney } from './money.pipe';

const nbsp = ' ';
const minus = '−';

describe('MoneyPipe', () => {
  const pipe = new MoneyPipe();

  it('writes Norwegian grouping and decimals with the ISO code after the number', () => {
    expect(pipe.transform(1234567.89, 'NOK')).toBe(`1${nbsp}234${nbsp}567,89${nbsp}NOK`);
  });

  it('uses a real minus sign for negative amounts', () => {
    expect(pipe.transform(-2150000, 'NOK')).toBe(`${minus}2${nbsp}150${nbsp}000,00${nbsp}NOK`);
  });

  it('uses the currency minor units', () => {
    expect(pipe.transform(125000, 'JPY', 0)).toBe(`125${nbsp}000${nbsp}JPY`);
    expect(pipe.transform(1.2345, 'BHD', 3)).toBe(`1,235${nbsp}BHD`);
  });

  it('rounds to the minor units for display', () => {
    expect(formatMoney(3.125, 'NOK')).toBe(`3,13${nbsp}NOK`);
  });

  it('writes nothing for a missing amount', () => {
    expect(pipe.transform(null, 'NOK')).toBe('');
    expect(pipe.transform(undefined, 'NOK')).toBe('');
    expect(pipe.transform(Number.NaN, 'NOK')).toBe('');
  });
});
