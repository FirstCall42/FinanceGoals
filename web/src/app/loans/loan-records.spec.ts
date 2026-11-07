import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { LoanRecords } from './loan-records';
import { SavedLoan } from './saved-loan.service';

const home: SavedLoan = { id: 7, name: 'Home', currencyCode: 'NOK', securedOnProperty: false };
const base = '/api/profiles/4/loans/7';

describe('Loan records', () => {
  let http: HttpTestingController;
  let changed: SavedLoan[];

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    changed = [];
  });

  afterEach(() => http.verify());

  function open() {
    const fixture = TestBed.createComponent(LoanRecords);
    fixture.componentRef.setInput('profileId', 4);
    fixture.componentRef.setInput('loans', [home]);
    fixture.componentInstance.changed.subscribe((l) => changed.push(l));
    fixture.detectChanges();
    return { fixture, page: fixture.nativeElement as HTMLElement };
  }

  it('marks a loan as secured on property', async () => {
    const { fixture, page } = open();
    const box = page.querySelector<HTMLInputElement>('input[type=checkbox]')!;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    const put = http.expectOne(
      (r) => r.method === 'PUT' && r.url === `${base}/secured-on-property`,
    );
    expect(put.request.body).toEqual({ securedOnProperty: true });
    put.flush({ ...home, securedOnProperty: true });
    await vi.waitFor(() => expect(changed).toEqual([{ ...home, securedOnProperty: true }]));
    fixture.detectChanges();
    expect(page.textContent).toContain('Home is secured on property.');
  });

  it('puts the box back and shows the message when the API refuses', async () => {
    const { fixture, page } = open();
    const box = page.querySelector<HTMLInputElement>('input[type=checkbox]')!;
    box.checked = true;
    box.dispatchEvent(new Event('change'));
    await fixture.whenStable();

    http
      .expectOne((r) => r.method === 'PUT')
      .flush({ title: 'Nope' }, { status: 500, statusText: 'Server Error' });
    await vi.waitFor(() => expect(box.checked).toBe(false));
    expect(changed).toEqual([]);
  });

  it('records a balance read as a Norwegian number, on the date picked', async () => {
    const { fixture, page } = open();
    const amount = page.querySelector<HTMLInputElement>('.balance input[inputmode=decimal]')!;
    amount.value = '2 900 000,50';
    amount.dispatchEvent(new Event('input'));
    const date = page.querySelector<HTMLInputElement>('app-date-field input[type=text]')!;
    date.value = '31.10.2026';
    date.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    page.querySelector<HTMLButtonElement>('.balance > button')!.click();
    await fixture.whenStable();

    const put = http.expectOne(
      (r) => r.method === 'PUT' && r.url === `${base}/balances/2026-10-31`,
    );
    expect(put.request.body).toEqual({ amount: 2_900_000.5 });
    put.flush({});
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(page.textContent).toContain('Recorded the balance of Home.');
    });
  });

  it('refuses a balance that is not a number without calling the API', async () => {
    const { fixture, page } = open();
    const amount = page.querySelector<HTMLInputElement>('.balance input[inputmode=decimal]')!;
    amount.value = 'abc';
    amount.dispatchEvent(new Event('input'));

    page.querySelector<HTMLButtonElement>('.balance > button')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(page.textContent).toContain('Home: enter the balance as a number');
    http.expectNone((r) => r.method === 'PUT');
  });
});
