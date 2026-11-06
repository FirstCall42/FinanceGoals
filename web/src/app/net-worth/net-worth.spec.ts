import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
import { DisplayCurrencyService } from '../currency/display-currency.service';
import { NetWorthPage } from './net-worth';

const nbsp = String.fromCharCode(0xa0);

const line = (over: object) => ({
  accountId: 1,
  name: 'Savings',
  type: 'Savings',
  currencyCode: 'NOK',
  amount: 50000,
  balanceDate: '2026-10-31',
  converted: 50000,
  rateDate: null,
  liquid: true,
  ...over,
});

const netWorth = (over: object = {}) => ({
  date: '2026-11-06',
  currency: 'NOK',
  total: 4_074_000,
  liquid: 58_000,
  missingRates: [],
  lines: [
    line({}),
    line({
      accountId: 2,
      name: 'House',
      type: 'Property',
      amount: 4_000_000,
      converted: 4_000_000,
      liquid: false,
    }),
  ],
  ...over,
});

const history = [
  {
    date: '2026-10-31',
    total: 4_000_000,
    liquid: 40_000,
    missingRates: [],
    rateDate: '2026-10-01',
  },
  {
    date: '2026-11-06',
    total: 4_074_000,
    liquid: 58_000,
    missingRates: ['RON'],
    rateDate: '2026-11-01',
  },
];

describe('Net worth page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const signIn = () =>
    TestBed.inject(AuthService).profiles.set([
      { id: 4, name: 'Household', role: 'Member', mainCurrency: 'NOK' },
    ]);

  const answer = (currency: string, current: object, points: readonly object[] = history) => {
    const nw = http.expectOne(
      (r) => r.url === '/api/profiles/4/net-worth' && r.params.get('currency') === currency,
    );
    nw.flush(current);
    http
      .expectOne(
        (r) =>
          r.url === '/api/profiles/4/net-worth/history' && r.params.get('currency') === currency,
      )
      .flush(points);
  };

  it('asks for a sign-in when signed out', async () => {
    const fixture = TestBed.createComponent(NetWorthPage);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Sign in to see');
    http.expectNone((r) => r.url.startsWith('/api/profiles'));
  });

  it('shows total and liquid net worth, the accounts and the month by month history', async () => {
    signIn();
    const fixture = TestBed.createComponent(NetWorthPage);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    answer('NOK', netWorth());

    await vi.waitFor(() => {
      fixture.detectChanges();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain(`4${nbsp}074${nbsp}000,00${nbsp}NOK`);
      expect(text).toContain(`58${nbsp}000,00${nbsp}NOK`);
      expect(text).toContain('Leaves out property and pensions');
      expect(text).toContain('House');
      expect(text).toContain('not liquid');
      expect(text).toContain('Month by month');
      expect(text).toContain('excl. RON');
      expect(text).toContain('The month 06.11.2026 leave out the accounts in RON');
      expect(text).toContain('the oldest used are from 01.10.2026');
      const chart = (fixture.nativeElement as HTMLElement).querySelector('svg[role=img]');
      expect(chart?.getAttribute('aria-label')).toContain('Net worth over time in NOK');
      expect(chart?.querySelectorAll('circle.hollow').length).toBe(1);
    });
  });

  it('warns about a missing rate and shows no amount for that account', async () => {
    signIn();
    const fixture = TestBed.createComponent(NetWorthPage);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    answer(
      'NOK',
      netWorth({
        total: 50000,
        liquid: 50000,
        missingRates: ['RON'],
        lines: [
          line({}),
          line({
            accountId: 3,
            name: 'Romania',
            currencyCode: 'RON',
            amount: 20000,
            converted: null,
          }),
        ],
      }),
      [],
    );

    await vi.waitFor(() => {
      fixture.detectChanges();
      const page = fixture.nativeElement as HTMLElement;
      expect(page.querySelector('app-missing-rate')?.textContent).toContain(
        'No exchange rate for RON',
      );
      expect(page.textContent).toContain('leave out the accounts in RON');
      expect(page.textContent).toContain('no rate');
      expect(page.textContent).toContain(`20${nbsp}000,00${nbsp}RON`);
    });
  });

  it('names the early months that lack a rate when today is complete', async () => {
    signIn();
    const fixture = TestBed.createComponent(NetWorthPage);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    const early = [
      { date: '2026-01-31', total: 10, liquid: 10, missingRates: ['RON'], rateDate: null },
      { date: '2026-02-28', total: 10, liquid: 10, missingRates: ['RON'], rateDate: null },
      { date: '2026-03-31', total: 30, liquid: 30, missingRates: [], rateDate: '2026-03-01' },
    ];
    answer('NOK', netWorth(), early);

    await vi.waitFor(() => {
      fixture.detectChanges();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('The months from 31.01.2026 to 28.02.2026');
      expect(text).toContain('too low for those months');
    });
  });

  it('reloads in the currency picked with the switcher', async () => {
    signIn();
    const fixture = TestBed.createComponent(NetWorthPage);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    answer('NOK', netWorth());
    await fixture.whenStable();

    TestBed.inject(DisplayCurrencyService).show('USD');
    await fixture.whenStable();

    await vi.waitFor(() =>
      answer('USD', netWorth({ currency: 'USD', total: 509_250, liquid: 7_250 })),
    );
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain(
        `509${nbsp}250,00${nbsp}USD`,
      );
    });
  });

  it('shows what the API says when it refuses', async () => {
    signIn();
    const fixture = TestBed.createComponent(NetWorthPage);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();

    http
      .expectOne((r) => r.url === '/api/profiles/4/net-worth')
      .flush(
        { errors: { currency: ['Pick one of the supported currencies.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
    http.expectOne((r) => r.url === '/api/profiles/4/net-worth/history').flush([]);

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(
        (fixture.nativeElement as HTMLElement).querySelector('[role=alert]')?.textContent,
      ).toContain('supported currencies');
    });
  });
});
