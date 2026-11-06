import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
import { Balances } from './balances';

const accounts = [
  { id: 1, name: 'Savings', type: 'Savings', currencyCode: 'NOK', archived: false },
  { id: 2, name: 'US retirement', type: 'Pension', currencyCode: 'USD', archived: false },
  { id: 3, name: 'Old deposit', type: 'Deposit', currencyCode: 'NOK', archived: true },
];
const balances = [
  { accountId: 1, date: '2025-12-31', amount: 50000 },
  { accountId: 2, date: '2025-12-31', amount: 350000.25 },
];

describe('Balances page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  /** Opens the page signed in, answering the three requests it makes. */
  async function open(values: readonly object[] = balances) {
    TestBed.inject(AuthService).profiles.set([
      { id: 4, name: 'Household', role: 'Member', mainCurrency: 'NOK' },
    ]);
    const fixture = TestBed.createComponent(Balances);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    http
      .expectOne((r) => r.urlWithParams === '/api/profiles/4/accounts?includeArchived=true')
      .flush(accounts);
    http.expectOne('/api/profiles/4/values').flush(values);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(inputs(fixture)).toHaveLength(2);
    });
    return fixture;
  }

  const inputs = (fixture: { nativeElement: unknown }) =>
    Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('.accounts input'),
    ) as HTMLInputElement[];

  const type = (input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  it('asks for a sign-in when signed out', async () => {
    const fixture = TestBed.createComponent(Balances);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Sign in to record');
    http.expectNone((r) => r.url.startsWith('/api/profiles'));
  });

  it('lists active accounts to fill in, with the last balance as a hint', async () => {
    const fixture = await open();

    const fields = inputs(fixture);
    expect(fields).toHaveLength(2);
    expect(fields[0].placeholder).toBe('last: 50 000');
    expect((fixture.nativeElement as HTMLElement).textContent).not.toContain('Old deposit ·');
  });

  it('records the filled-in accounts, skips blank ones and reads Norwegian numbers', async () => {
    const fixture = await open([]);
    const page = fixture.nativeElement as HTMLElement;
    const date = page.querySelector('input[type=date]') as HTMLInputElement;
    date.value = '2026-01-31';
    date.dispatchEvent(new Event('input'));
    await fixture.whenStable();
    const [savings, usd] = inputs(fixture);
    type(savings, '51 000,50');
    type(usd, '');

    page.querySelector('form')!.dispatchEvent(new Event('submit'));

    const post = http.expectOne(
      (r) => r.method === 'POST' && r.url === '/api/profiles/4/snapshots',
    );
    expect(post.request.body).toEqual({
      date: '2026-01-31',
      values: [{ accountId: 1, amount: 51000.5 }],
    });
    post.flush({ date: '2026-01-31', count: 1 });
    await vi.waitFor(() => {
      http
        .expectOne((r) => r.urlWithParams === '/api/profiles/4/accounts?includeArchived=true')
        .flush(accounts);
    });
    http.expectOne('/api/profiles/4/values').flush([]);
  });

  it('refuses an empty snapshot and a text that is not a number, without calling the API', async () => {
    const fixture = await open([]);
    const page = fixture.nativeElement as HTMLElement;

    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.querySelector('[role=alert]')?.textContent).toContain('at least one account');

    type(inputs(fixture)[0], 'lots');
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(page.querySelector('[role=alert]')?.textContent).toContain("isn't a number");
  });

  it('shows the history by date and deletes one balance', async () => {
    const fixture = await open();
    const page = fixture.nativeElement as HTMLElement;

    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.querySelector('.day')?.textContent).toContain('350');
    });
    const remove = page.querySelector('button[aria-label^="Delete Savings"]') as HTMLButtonElement;
    remove.click();

    const del = http.expectOne((r) => r.method === 'DELETE');
    expect(del.request.url).toBe('/api/profiles/4/accounts/1/values/2025-12-31');
    del.flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => {
      http
        .expectOne((r) => r.urlWithParams === '/api/profiles/4/accounts?includeArchived=true')
        .flush(accounts);
    });
    http.expectOne('/api/profiles/4/values').flush([balances[1]]);
  });

  it('shows what the API says when a snapshot is refused', async () => {
    const fixture = await open([]);
    const page = fixture.nativeElement as HTMLElement;
    type(inputs(fixture)[0], '1,234');

    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    http
      .expectOne((r) => r.method === 'POST')
      .flush(
        {
          errors: {
            values: ['A balance in NOK must be a whole number of minor units (øre, cents).'],
          },
        },
        { status: 400, statusText: 'Bad Request' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(page.querySelector('[role=alert]')?.textContent).toContain('minor units');
  });
});
