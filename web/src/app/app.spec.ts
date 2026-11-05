import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';

describe('App routes', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('home page shows the API health', async () => {
    const harness = await RouterTestingHarness.create('/');
    const http = TestBed.inject(HttpTestingController);

    http.expectOne('/health/live').flush('Healthy');
    http.expectOne('/health/ready').flush('Healthy');

    const page = harness.routeNativeElement as HTMLElement;
    expect(page.querySelector('h2')?.textContent).toBe('API health');
    // The result arrives asynchronously after the responses; wait for the page to show it.
    await vi.waitFor(() =>
      expect(page.querySelector('[data-testid="database"]')?.textContent?.trim()).toBe('OK'),
    );
  });

  it('unknown paths go to the home page', async () => {
    const harness = await RouterTestingHarness.create('/no-such-page');

    const requests = TestBed.inject(HttpTestingController).match(() => true);
    expect(requests.map((request) => request.request.url).sort()).toEqual([
      '/health/live',
      '/health/ready',
    ]);
    requests.forEach((request) => request.flush('Healthy'));
    expect((harness.routeNativeElement as HTMLElement).querySelector('h2')?.textContent).toBe(
      'API health',
    );
  });
});
