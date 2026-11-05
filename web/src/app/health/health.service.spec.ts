import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { HealthService } from './health.service';

describe('HealthService', () => {
  let service: HealthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(HealthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function answer(live: number, ready: number) {
    const result = service.check();
    respond('/health/live', live);
    respond('/health/ready', ready);
    return result;
  }

  function respond(path: string, status: number) {
    const request = http.expectOne(path);
    expect(request.request.method).toBe('GET');
    if (status === 0) {
      request.error(new ProgressEvent('error'));
    } else {
      request.flush(status === 200 ? 'Healthy' : 'Unhealthy', {
        status,
        statusText: String(status),
      });
    }
  }

  it('reports both up when the API and database answer', async () => {
    expect(await answer(200, 200)).toEqual({ api: 'up', database: 'up' });
  });

  it('reports the database down when readiness answers 503', async () => {
    expect(await answer(200, 503)).toEqual({ api: 'up', database: 'down' });
  });

  it('reports unreachable when the API does not answer', async () => {
    expect(await answer(0, 0)).toEqual({ api: 'unreachable', database: 'unreachable' });
  });

  it('treats any other error status as unreachable, not down', async () => {
    expect(await answer(502, 500)).toEqual({ api: 'unreachable', database: 'unreachable' });
  });
});
