import { Component, computed, input } from '@angular/core';
import { NetWorthPoint } from './net-worth.service';

const width = 640;
const height = 220;
const pad = { left: 8, right: 8, top: 12, bottom: 20 };

/** Net worth and liquid net worth over time as two lines; a hollow dot marks a month with a missing rate. */
@Component({
  selector: 'app-net-worth-chart',
  template: `
    @if (shape(); as s) {
      <svg
        [attr.viewBox]="'0 0 ' + w + ' ' + h"
        role="img"
        [attr.aria-label]="
          'Net worth over time in ' + currency() + ', from ' + s.from + ' to ' + s.to
        "
      >
        <line
          class="axis"
          [attr.x1]="pad.left"
          [attr.x2]="w - pad.right"
          [attr.y1]="s.zero"
          [attr.y2]="s.zero"
        />
        <polyline class="liquid" [attr.points]="s.liquid" />
        <polyline class="total" [attr.points]="s.total" />
        @for (d of s.dots; track d.x) {
          <circle class="dot" [class.hollow]="d.missing" [attr.cx]="d.x" [attr.cy]="d.y" r="3.5" />
        }
        <text class="tick" [attr.x]="pad.left" [attr.y]="h - 4">{{ s.from }}</text>
        <text class="tick" text-anchor="end" [attr.x]="w - pad.right" [attr.y]="h - 4">
          {{ s.to }}
        </text>
      </svg>
      <p class="legend">
        <span class="key total"></span> Net worth <span class="key liquid"></span> Liquid
        @if (s.anyMissing) {
          <span class="key hollow"></span> Left out a currency without a rate
        }
      </p>
    }
  `,
  styles: `
    svg {
      width: 100%;
      height: auto;
      max-height: 260px;
    }
    .axis {
      stroke: var(--fg-line);
    }
    polyline {
      fill: none;
      stroke-width: 2.5;
      stroke-linejoin: round;
    }
    .total {
      stroke: var(--fg-accent);
    }
    .liquid {
      stroke: var(--fg-muted);
      stroke-dasharray: 6 4;
    }
    .dot {
      fill: var(--fg-accent);
      stroke: var(--fg-accent);
      stroke-width: 2;
    }
    .dot.hollow {
      fill: var(--fg-surface);
    }
    .tick {
      fill: var(--fg-muted);
      font-size: 11px;
    }
    .legend {
      margin: 4px 0 0;
      font-size: 13px;
      color: var(--fg-muted);
    }
    .key {
      display: inline-block;
      width: 14px;
      height: 0;
      border-top: 3px solid var(--fg-accent);
      vertical-align: middle;
      margin: 0 4px 0 10px;
    }
    .key:first-child {
      margin-left: 0;
    }
    .key.liquid {
      border-top: 3px dashed var(--fg-muted);
    }
    .key.hollow {
      width: 8px;
      height: 8px;
      border: 2px solid var(--fg-accent);
      border-radius: 50%;
    }
  `,
})
export class NetWorthChart {
  readonly points = input.required<readonly NetWorthPoint[]>();
  readonly currency = input.required<string>();

  protected readonly w = width;
  protected readonly h = height;
  protected readonly pad = pad;

  protected readonly shape = computed(() => {
    const pts = this.points();
    if (pts.length < 2) {
      return undefined;
    }
    const time = pts.map((p) => Date.parse(`${p.date}T00:00:00Z`));
    const values = pts.flatMap((p) => [p.total, p.liquid]);
    const lo = Math.min(0, ...values);
    const hi = Math.max(0, ...values);
    const span = hi - lo || 1;
    const t0 = time[0];
    const tSpan = time[time.length - 1] - t0 || 1;
    const x = (i: number) => pad.left + ((time[i] - t0) / tSpan) * (width - pad.left - pad.right);
    const y = (v: number) => pad.top + (1 - (v - lo) / span) * (height - pad.top - pad.bottom);
    const line = (pick: (p: NetWorthPoint) => number) =>
      pts.map((p, i) => `${x(i).toFixed(1)},${y(pick(p)).toFixed(1)}`).join(' ');
    return {
      total: line((p) => p.total),
      liquid: line((p) => p.liquid),
      zero: y(0),
      dots: pts.map((p, i) => ({ x: x(i), y: y(p.total), missing: p.missingRates.length > 0 })),
      anyMissing: pts.some((p) => p.missingRates.length > 0),
      from: pts[0].date,
      to: pts[pts.length - 1].date,
    };
  });
}
