import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Settings } from '../settings/settings';
import { CurrencySwitcher } from './currency-switcher';
import { CurrencyInfo, CurrencyService } from './currency.service';
import { DisplayCurrencyService } from './display-currency.service';

const list: CurrencyInfo[] = [
  { code: 'EUR', numericCode: 978, minorUnits: 2, name: 'Euro' },
  { code: 'NOK', numericCode: 578, minorUnits: 2, name: 'Norwegian Krone' },
  { code: 'USD', numericCode: 840, minorUnits: 2, name: 'US Dollar' },
];

describe('Main and display currency', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('starts with NOK as the main and display currency', () => {
    const display = TestBed.inject(DisplayCurrencyService);

    expect(display.main()).toBe('NOK');
    expect(display.current()).toBe('NOK');
  });

  it('remembers a new main currency and shows amounts in it', () => {
    TestBed.inject(DisplayCurrencyService).setMain('EUR');

    expect(localStorage.getItem('financegoals.mainCurrency')).toBe('EUR');
    expect(TestBed.inject(DisplayCurrencyService).current()).toBe('EUR');
  });

  it('switches the display currency for this visit only', () => {
    const display = TestBed.inject(DisplayCurrencyService);

    display.show('USD');

    expect(display.current()).toBe('USD');
    expect(display.main()).toBe('NOK');
    expect(localStorage.getItem('financegoals.mainCurrency')).toBeNull();
  });

  it('ignores codes that are not ISO currency codes', () => {
    localStorage.setItem('financegoals.mainCurrency', 'krone');
    const display = TestBed.inject(DisplayCurrencyService);

    display.show('usd');
    display.setMain('');

    expect(display.main()).toBe('NOK');
    expect(display.current()).toBe('NOK');
  });

  it('loads the currency list once', async () => {
    const service = TestBed.inject(CurrencyService);

    const first = service.load();
    const second = service.load();
    http.expectOne('/api/currencies').flush(list);
    await Promise.all([first, second]);

    expect(service.currencies().map((c) => c.code)).toEqual(['EUR', 'NOK', 'USD']);
  });

  it('switcher lists the main currency first and switches the display currency', async () => {
    const fixture = TestBed.createComponent(CurrencySwitcher);
    http.expectOne('/api/currencies').flush(list);
    await fixture.whenStable();
    fixture.detectChanges();

    const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
    const options = Array.from(select.options).map((o) => o.textContent?.trim());
    expect(options).toEqual(['NOK, Norwegian Krone (main)', 'EUR, Euro', 'USD, US Dollar']);

    select.value = 'USD';
    select.dispatchEvent(new Event('change'));
    expect(TestBed.inject(DisplayCurrencyService).current()).toBe('USD');
  });

  it('settings change the main currency', async () => {
    const fixture = TestBed.createComponent(Settings);
    http.expectOne('/api/currencies').flush(list);
    await fixture.whenStable();
    fixture.detectChanges();

    const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
    select.value = 'EUR';
    select.dispatchEvent(new Event('change'));

    expect(TestBed.inject(DisplayCurrencyService).main()).toBe('EUR');
  });
});
