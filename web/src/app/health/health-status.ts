import { Component, inject, signal } from '@angular/core';
import { ApiHealth, HealthService, ProbeState } from './health.service';

const labels: Record<ProbeState, string> = {
  up: 'OK',
  down: 'Not ready',
  unreachable: 'Unreachable',
};

/** Shows whether the API answers and whether it can reach its database. */
@Component({
  selector: 'app-health-status',
  templateUrl: './health-status.html',
  styleUrl: './health-status.scss',
})
export class HealthStatus {
  private readonly health = inject(HealthService);

  protected readonly result = signal<ApiHealth | undefined>(undefined);
  protected readonly checking = signal(false);

  constructor() {
    void this.refresh();
  }

  protected label(state: ProbeState): string {
    return labels[state];
  }

  protected async refresh(): Promise<void> {
    this.checking.set(true);
    try {
      this.result.set(await this.health.check());
    } finally {
      this.checking.set(false);
    }
  }
}
