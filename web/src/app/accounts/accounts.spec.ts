import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
import { Accounts } from './accounts';

const savings = {
  id: 1,
  name: 'Buffer',
  type: 'Savings',
  currencyCode: 'NOK',
  archived: false,
};

describe('Accounts page', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    // The expected-growth section asks for the saved assumptions; these tests are about the list.
    http.match('/api/profiles/4/account-assumptions').forEach((r) => r.flush([]));
    http.verify();
  });

  const signIn = () =>
    TestBed.inject(AuthService).profiles.set([
      { id: 4, name: 'Household', role: 'Member', mainCurrency: 'NOK' },
    ]);

  const list = (archived = false) =>
    http.expectOne(
      (r) =>
        r.method === 'GET' &&
        r.urlWithParams === `/api/profiles/4/accounts?includeArchived=${archived}`,
    );

  it('asks for a sign-in and calls nothing when signed out', async () => {
    const fixture = TestBed.createComponent(Accounts);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Sign in to see');
    http.expectNone((r) => r.url.startsWith('/api/profiles'));
  });

  it('lists the accounts of the household', async () => {
    signIn();
    const fixture = TestBed.createComponent(Accounts);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();

    list().flush([savings]);
    await fixture.whenStable();
    fixture.detectChanges();

    await vi.waitFor(() => {
      fixture.detectChanges();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Buffer');
      expect(text).toContain('Savings account · NOK');
    });
  });

  it('adds an account and reloads the list', async () => {
    signIn();
    const fixture = TestBed.createComponent(Accounts);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    list().flush([]);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const name = page.querySelector('.add input') as HTMLInputElement;
    name.value = '  Stocks  ';
    name.dispatchEvent(new Event('input'));

    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    const post = http.expectOne((r) => r.method === 'POST' && r.url === '/api/profiles/4/accounts');
    expect(post.request.body).toEqual({ name: 'Stocks', type: 'Current', currencyCode: 'NOK' });
    post.flush({ ...savings, id: 2, name: 'Stocks', type: 'Current' });
    await vi.waitFor(() => list().flush([savings]));
  });

  it('archives an account', async () => {
    signIn();
    const fixture = TestBed.createComponent(Accounts);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    list().flush([savings]);
    await fixture.whenStable();
    fixture.detectChanges();

    await vi.waitFor(() => {
      fixture.detectChanges();
      const archive = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Archive');
      expect(archive).toBeDefined();
      archive!.click();
    });

    const patch = http.expectOne(
      (r) => r.method === 'PATCH' && r.url === '/api/profiles/4/accounts/1',
    );
    expect(patch.request.body).toEqual({ archived: true });
    patch.flush({ ...savings, archived: true });
    await vi.waitFor(() => list().flush([]));
  });

  it('shows the API message when the name is taken', async () => {
    signIn();
    const fixture = TestBed.createComponent(Accounts);
    http.expectOne('/api/currencies').flush([]);
    await fixture.whenStable();
    list().flush([savings]);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const name = page.querySelector('.add input') as HTMLInputElement;
    name.value = 'Buffer';
    name.dispatchEvent(new Event('input'));

    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    http
      .expectOne((r) => r.method === 'POST')
      .flush(
        { title: 'An account with that name already exists.' },
        { status: 409, statusText: 'Conflict' },
      );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(page.querySelector('[role=alert]')?.textContent).toContain('already exists');
  });
});
