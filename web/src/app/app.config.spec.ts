import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApplicationInitStatus } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { appConfig } from './app.config';
import { ApiConfiguration } from './api/api-configuration';

describe('App startup', () => {
  it('reads config.json and points the generated API client at the configured API', async () => {
    TestBed.configureTestingModule({
      providers: [...appConfig.providers, provideHttpClientTesting()],
    });
    const http = TestBed.inject(HttpTestingController);
    const started = TestBed.inject(ApplicationInitStatus).donePromise;

    http.expectOne('/config.json').flush({ apiBaseUrl: 'https://api.example.com/' });
    await started;

    expect(TestBed.inject(ApiConfiguration).rootUrl).toBe('https://api.example.com');
  });
});
