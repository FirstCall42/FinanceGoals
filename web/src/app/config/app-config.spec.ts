import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AppConfig } from './app-config';

describe('AppConfig', () => {
  let config: AppConfig;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    config = TestBed.inject(AppConfig);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function load(respond: (request: ReturnType<HttpTestingController['expectOne']>) => void) {
    const loading = config.load();
    respond(http.expectOne('/config.json'));
    await loading;
  }

  it('uses the API base URL from config.json, without a trailing slash', async () => {
    await load((request) => request.flush({ apiBaseUrl: 'https://api.example/' }));

    expect(config.apiUrl('/health/live')).toBe('https://api.example/health/live');
  });

  it('keeps requests relative when config.json is missing', async () => {
    await load((request) => request.flush('Not found', { status: 404, statusText: 'Not Found' }));

    expect(config.apiUrl('/health/live')).toBe('/health/live');
  });

  it('ignores an apiBaseUrl that is not a string', async () => {
    await load((request) => request.flush({ apiBaseUrl: 42 }));

    expect(config.apiUrl('/health/live')).toBe('/health/live');
  });
});
