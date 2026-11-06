import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from '../auth/auth.service';
import { HouseholdSettings } from './household-settings';
import { ProfileService } from './profile.service';

describe('Household profile', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('accepting a link that no longer works shows the API message', async () => {
    const accepting = TestBed.inject(ProfileService).accept('abc');
    http
      .expectOne((r) => r.url === '/api/invites/accept' && r.body.token === 'abc')
      .flush(
        { title: "This invite link doesn't work. It may have been used, withdrawn or expired." },
        { status: 404, statusText: 'Not Found' },
      );

    await expect(accepting).rejects.toThrow("This invite link doesn't work");
  });

  it('builds the link from this app and the token', () => {
    expect(TestBed.inject(ProfileService).link('t0k-en')).toBe(
      `${window.location.origin}/invite/t0k-en`,
    );
  });

  it('an owner sees the members, creates an invite and gets the link to send', async () => {
    TestBed.inject(AuthService).profiles.set([
      { id: 4, name: 'Household', role: 'Owner', mainCurrency: 'NOK' },
    ]);
    const fixture = TestBed.createComponent(HouseholdSettings);
    await fixture.whenStable();
    http
      .expectOne('/api/profiles/4/members')
      .flush([{ userId: 1, email: 'owner@example.com', name: 'Owner', role: 'Owner' }]);
    await fixture.whenStable();
    http.expectOne('/api/profiles/4/invites').flush([]);
    await fixture.whenStable();
    const page = fixture.nativeElement as HTMLElement;
    const input = page.querySelector('input[type=email]') as HTMLInputElement;
    input.value = 'partner@example.com';
    input.dispatchEvent(new Event('input'));

    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    const created = http.expectOne('/api/profiles/4/invites');
    expect(created.request.method).toBe('POST');
    expect(created.request.body).toEqual({ email: 'partner@example.com' });
    created.flush({
      id: 9,
      email: 'partner@example.com',
      expiresAt: '2026-11-13T10:00:00Z',
      token: 'secret-token',
    });
    await fixture.whenStable();
    http
      .expectOne('/api/profiles/4/invites')
      .flush([{ id: 9, email: 'partner@example.com', expiresAt: '2026-11-13T10:00:00Z' }]);
    await fixture.whenStable();

    const link = page.querySelector('input[aria-label="Invite link"]') as HTMLInputElement;
    expect(link.value).toBe(`${window.location.origin}/invite/secret-token`);
    await vi.waitFor(() => expect(page.textContent).toContain('Waiting to join'));
    expect(page.textContent).toContain('owner@example.com');
  });

  it('a member sees the household but cannot invite', async () => {
    TestBed.inject(AuthService).profiles.set([
      { id: 4, name: 'Household', role: 'Member', mainCurrency: 'NOK' },
    ]);
    const fixture = TestBed.createComponent(HouseholdSettings);
    await fixture.whenStable();
    http.expectOne('/api/profiles/4/members').flush([]);
    await fixture.whenStable();

    expect((fixture.nativeElement as HTMLElement).querySelector('form')).toBeNull();
    http.expectNone('/api/profiles/4/invites');
  });
});
