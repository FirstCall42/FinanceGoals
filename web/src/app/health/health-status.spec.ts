import { TestBed } from '@angular/core/testing';
import { HealthStatus } from './health-status';
import { ApiHealth, HealthService } from './health.service';

describe('HealthStatus', () => {
  let results: ApiHealth[];
  let checks: number;

  beforeEach(async () => {
    checks = 0;
    await TestBed.configureTestingModule({
      imports: [HealthStatus],
      providers: [
        {
          provide: HealthService,
          useValue: {
            check: () => Promise.resolve(results[Math.min(checks++, results.length - 1)]),
          } satisfies Pick<HealthService, 'check'>,
        },
      ],
    }).compileComponents();
  });

  async function render() {
    const fixture = TestBed.createComponent(HealthStatus);
    await fixture.whenStable();
    return { fixture, element: fixture.nativeElement as HTMLElement };
  }

  function text(element: HTMLElement, testId: string) {
    return element.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim();
  }

  it('shows the API and database as OK when both are up', async () => {
    results = [{ api: 'up', database: 'up' }];

    const { element } = await render();

    expect(text(element, 'api')).toBe('OK');
    expect(text(element, 'database')).toBe('OK');
  });

  it('shows the database as not ready when only the API is up', async () => {
    results = [{ api: 'up', database: 'down' }];

    const { element } = await render();

    expect(text(element, 'api')).toBe('OK');
    expect(text(element, 'database')).toBe('Not ready');
  });

  it('shows both as unreachable when the API does not answer', async () => {
    results = [{ api: 'unreachable', database: 'unreachable' }];

    const { element } = await render();

    expect(text(element, 'api')).toBe('Unreachable');
    expect(text(element, 'database')).toBe('Unreachable');
  });

  it('checks again when asked', async () => {
    results = [
      { api: 'unreachable', database: 'unreachable' },
      { api: 'up', database: 'up' },
    ];
    const { fixture, element } = await render();

    element.querySelector('button')!.click();
    await fixture.whenStable();

    expect(checks).toBe(2);
    expect(text(element, 'api')).toBe('OK');
  });
});
