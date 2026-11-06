import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
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

  // Settings also shows the household, which loads its members and then its invites.
  const answerHousehold = async (fixture: { whenStable(): Promise<unknown> }) => {
    for (let round = 0; round < 3; round++) {
      http
        .match((r) => r.method === 'GET' && r.url.startsWith('/api/profiles/4/'))
        .forEach((r) => r.flush([]));
      await fixture.whenStable();
    }
  };

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

  it('takes the main currency from the household profile', () => {
    const auth = TestBed.inject(AuthService);
    const display = TestBed.inject(DisplayCurrencyService);

    auth.profiles.set([{ id: 1, name: 'Household', role: 'Member', mainCurrency: 'EUR' }]);

    expect(display.main()).toBe('EUR');
    expect(display.current()).toBe('EUR');
    auth.profiles.set([]);
    expect(display.main()).toBe('NOK');
  });

  it('switches the display currency for this visit only', () => {
    const display = TestBed.inject(DisplayCurrencyService);

    display.show('USD');

    expect(display.current()).toBe('USD');
    expect(display.main()).toBe('NOK');
    display.reset();
    expect(display.current()).toBe('NOK');
  });

  it('ignores codes that are not ISO currency codes', () => {
    const display = TestBed.inject(DisplayCurrencyService);

    display.show('usd');

    expect(display.current()).toBe('NOK');
  });

  it('drops the old browser setting', () => {
    localStorage.setItem('financegoals.mainCurrency', 'EUR');

    TestBed.inject(DisplayCurrencyService);

    expect(localStorage.getItem('financegoals.mainCurrency')).toBeNull();
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

  it('an owner changes the household main currency in settings', async () => {
    const auth = TestBed.inject(AuthService);
    auth.profiles.set([{ id: 4, name: 'Household', role: 'Owner', mainCurrency: 'NOK' }]);
    auth.token.set('t');
    const fixture = TestBed.createComponent(Settings);
    http.expectOne('/api/currencies').flush(list);
    await fixture.whenStable();
    await answerHousehold(fixture);
    fixture.detectChanges();

    const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
    expect(select.disabled).toBe(false);
    select.value = 'EUR';
    select.dispatchEvent(new Event('change'));
    const patch = http.expectOne('/api/profiles/4');
    expect(patch.request.method).toBe('PATCH');
    expect(patch.request.body).toEqual({ mainCurrency: 'EUR' });
    patch.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => http.expectOne('/api/me'));
  });

  it('a member sees the main currency but cannot change it', async () => {
    TestBed.inject(AuthService).profiles.set([
      { id: 4, name: 'Household', role: 'Member', mainCurrency: 'EUR' },
    ]);
    const fixture = TestBed.createComponent(Settings);
    http.expectOne('/api/currencies').flush(list);
    await fixture.whenStable();
    await answerHousehold(fixture);
    fixture.detectChanges();

    const select = (fixture.nativeElement as HTMLElement).querySelector('select')!;
    expect(select.disabled).toBe(true);
    expect(select.value).toBe('EUR');
  });
});
