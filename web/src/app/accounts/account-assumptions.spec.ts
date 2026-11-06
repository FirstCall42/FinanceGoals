import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AccountAssumptions } from './account-assumptions';
import { Account } from './account.service';

const fund: Account = {
  id: 2,
  name: 'Index fund',
  type: 'Brokerage',
  currencyCode: 'USD',
  archived: false,
};
const buffer: Account = {
  id: 1,
  name: 'Buffer',
  type: 'Savings',
  currencyCode: 'NOK',
  archived: false,
};
const url = '/api/profiles/4/account-assumptions';

describe('Expected growth', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function open() {
    const fixture = TestBed.createComponent(AccountAssumptions);
    fixture.componentRef.setInput('profileId', 4);
    fixture.componentRef.setInput('accounts', [buffer, fund]);
    fixture.detectChanges();
    await fixture.whenStable();
    http
      .expectOne(url)
      .flush([{ accountId: 2, expectedYearlyReturnPercent: 6.5, monthlyContribution: 1500 }]);
    await vi.waitFor(() => {
      fixture.detectChanges();
      expect(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>('input')[2].value,
      ).toBe('6.5');
    });
    return fixture;
  }

  const type = (input: HTMLInputElement, value: string) => {
    input.value = value;
    input.dispatchEvent(new Event('input'));
  };

  it('shows what is saved for each account and the currency of the contribution', async () => {
    const fixture = await open();
    const page = fixture.nativeElement as HTMLElement;
    const inputs = page.querySelectorAll<HTMLInputElement>('input');

    expect(inputs[0].value).toBe('');
    expect(inputs[3].value).toBe('1500');
    expect(page.textContent).toContain('Per month (USD)');
  });

  it('saves a return and a contribution, reading Norwegian numbers', async () => {
    const fixture = await open();
    const page = fixture.nativeElement as HTMLElement;
    const inputs = page.querySelectorAll<HTMLInputElement>('input');
    type(inputs[0], '4,5');
    type(inputs[1], '2 000');
    fixture.detectChanges();

    page.querySelector<HTMLButtonElement>('button.primary')!.click();
    await fixture.whenStable();

    const put = http.expectOne(
      (r) => r.method === 'PUT' && r.url === '/api/profiles/4/accounts/1/assumption',
    );
    expect(put.request.body).toEqual({
      expectedYearlyReturnPercent: 4.5,
      monthlyContribution: 2000,
    });
    put.flush({ accountId: 1, expectedYearlyReturnPercent: 4.5, monthlyContribution: 2000 });
    await vi.waitFor(() => http.expectOne(url).flush([]));
  });

  it('clears an account', async () => {
    const fixture = await open();
    const page = fixture.nativeElement as HTMLElement;

    page.querySelectorAll<HTMLButtonElement>('button.secondary')[1].click();
    await fixture.whenStable();

    http
      .expectOne((r) => r.method === 'DELETE' && r.url === '/api/profiles/4/accounts/2/assumption')
      .flush(null, { status: 204, statusText: 'No Content' });
    await vi.waitFor(() => http.expectOne(url).flush([]));
  });

  it('refuses text that is not a number without calling the API', async () => {
    const fixture = await open();
    const page = fixture.nativeElement as HTMLElement;
    type(page.querySelectorAll<HTMLInputElement>('input')[0], 'abc');
    fixture.detectChanges();

    page.querySelector<HTMLButtonElement>('button.primary')!.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(page.querySelector('[role=alert]')?.textContent).toContain('Buffer');
    http.expectNone((r) => r.method === 'PUT');
  });
});
