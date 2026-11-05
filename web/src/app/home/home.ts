import { Component } from '@angular/core';
import { HealthStatus } from '../health/health-status';

@Component({
  selector: 'app-home',
  imports: [HealthStatus],
  template: `<app-health-status />`,
})
export class Home {}
