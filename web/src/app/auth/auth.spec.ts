import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { firstValueFrom } from 'rxjs';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';
import { tokenExpiry } from './google-identity';

/** An unsigned token with only an expiry: the browser never checks signatures, the API does. */
function token(expiresInSeconds: number): string {
  const payload = btoa(JSON.stringify({ exp: Math.floor(Date.now() / 1000) + expiresInSeconds }))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '');
  return `header.${payload}.signature`;
}

describe('Sign-in', () => {
  let http: HttpTestingController;
  let auth: AuthService;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
  });

  afterEach(() => http.verify());

  it('reads the expiry of a token', () => {
    const valid = token(3600);

    expect(tokenExpiry(valid)).toBeGreaterThan(Date.now() / 1000);
    expect(tokenExpiry('garbage')).toBe(0);
  });

  it('signs in through the API and remembers the token for this tab', async () => {
    const credential = token(3600);

    const signingIn = auth.signIn(credential);
    const request = http.expectOne('/api/me');
    expect(request.request.method).toBe('POST');
    expect(request.request.headers.get('Authorization')).toBe(`Bearer ${credential}`);
    request.flush({ id: 7, email: 'owner@example.com', name: 'Owner' });
    await signingIn;

    expect(auth.user()).toEqual({ id: 7, email: 'owner@example.com', name: 'Owner' });
    expect(sessionStorage.getItem('financegoals.idToken')).toBe(credential);
  });

  it('does not use an expired token', async () => {
    await auth.signIn(token(-60));

    http.expectNone('/api/me');
    expect(auth.token()).toBeUndefined();
  });

  it('sends the token only to our API, and a 401 signs out', async () => {
    const signingIn = auth.signIn(token(3600));
    http.expectOne('/api/me').flush({ id: 7, email: 'owner@example.com', name: null });
    await signingIn;
    const client = TestBed.inject(HttpClient);

    void firstValueFrom(client.get('https://example.com/data')).catch(() => undefined);
    expect(http.expectOne('https://example.com/data').request.headers.has('Authorization')).toBe(
      false,
    );

    const refused = firstValueFrom(client.get('/api/accounts')).catch(() => undefined);
    http.expectOne('/api/accounts').flush(null, { status: 401, statusText: 'Unauthorized' });
    await refused;

    expect(auth.signedIn()).toBe(false);
    expect(sessionStorage.getItem('financegoals.idToken')).toBeNull();
  });
});
