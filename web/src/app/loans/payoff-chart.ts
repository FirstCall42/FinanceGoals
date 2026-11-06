import { Component, computed, input } from '@angular/core';
import { ScheduleRow } from './loan-calculator.service';

const width = 640;
const height = 240;
const pad = { left: 8, right: 8, top: 22, bottom: 20 };

interface Point {
  readonly time: number;
  readonly balance: number;
}

/** What is left to pay over time: the standard schedule and the one with extra payments, two curves. */
@Component({
  selector: 'app-payoff-chart',
  template: `
    @if (shape(); as s) {
      <svg
        [attr.viewBox]="'0 0 ' + w + ' ' + h"
        role="img"
        [attr.aria-label]="
          'Remaining balance in ' +
          currency() +
          ': paid off ' +
          s.standardEnd +
          ' without extras, ' +
          s.extraEnd +
          ' with extras'
        "
      >
        <line
          class="axis"
          [attr.x1]="pad.left"
          [attr.x2]="w - pad.right"
          [attr.y1]="s.zero"
          [attr.y2]="s.zero"
        />
        <polyline class="standard" [attr.points]="s.standard" />
        <polyline class="extra" [attr.points]="s.extra" />
        <text class="tick" [attr.x]="pad.left" [attr.y]="12">{{ s.top }}</text>
        <text class="tick" [attr.x]="pad.left" [attr.y]="h - 4">{{ s.from }}</text>
        <text class="tick" text-anchor="end" [attr.x]="w - pad.right" [attr.y]="h - 4">
          {{ s.to }}
        </text>
      </svg>
      <p class="legend">
        <span class="key standard"></span> Without extras <span class="key extra"></span> With
        extras
      </p>
    }
  `,
  styles: `
    svg {
      width: 100%;
      height: auto;
      max-height: 280px;
    }
    .axis {
      stroke: var(--fg-line);
    }
    polyline {
      fill: none;
      stroke-width: 2.5;
      stroke-linejoin: round;
    }
    .standard {
      stroke: var(--fg-muted);
      stroke-dasharray: 6 4;
    }
    .extra {
      stroke: var(--fg-accent);
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
      vertical-align: middle;
      margin: 0 4px 0 10px;
    }
    .key:first-child {
      margin-left: 0;
    }
    .key.standard {
      border-top: 3px dashed var(--fg-muted);
    }
    .key.extra {
      border-top: 3px solid var(--fg-accent);
    }
  `,
})
export class PayoffChart {
  readonly standard = input.required<readonly ScheduleRow[]>();
  readonly withExtras = input.required<readonly ScheduleRow[]>();
  readonly currency = input.required<string>();

  protected readonly w = width;
  protected readonly h = height;
  protected readonly pad = pad;

  protected readonly shape = computed(() => {
    const a = curve(this.standard());
    const b = curve(this.withExtras());
    if (a.length < 2 || b.length < 2) {
      return undefined;
    }
    const t0 = Math.min(a[0].time, b[0].time);
    const t1 = Math.max(a[a.length - 1].time, b[b.length - 1].time);
    const top = Math.max(...a.map((p) => p.balance), ...b.map((p) => p.balance)) || 1;
    const x = (t: number) =>
      pad.left + ((t - t0) / (t1 - t0 || 1)) * (width - pad.left - pad.right);
    const y = (v: number) => pad.top + (1 - v / top) * (height - pad.top - pad.bottom);
    const line = (pts: readonly Point[]) =>
      pts.map((p) => `${x(p.time).toFixed(1)},${y(p.balance).toFixed(1)}`).join(' ');
    return {
      standard: line(a),
      extra: line(b),
      zero: y(0),
      top: `${Math.round(top).toLocaleString('nb-NO')} ${this.currency()}`,
      from: monthYear(t0),
      to: monthYear(t1),
      standardEnd: monthYear(a[a.length - 1].time),
      extraEnd: monthYear(b[b.length - 1].time),
    };
  });
}

/** The balance after each payment, starting from what was owed before the first. */
function curve(rows: readonly ScheduleRow[]): Point[] {
  if (rows.length === 0) {
    return [];
  }
  const time = (r: ScheduleRow) => Date.parse(`${r.date}T00:00:00Z`);
  return [
    { time: time(rows[0]), balance: rows[0].openingBalance },
    ...rows.map((r) => ({ time: time(r), balance: r.balance })),
  ];
}

function monthYear(time: number): string {
  return new Date(time).toLocaleDateString('nb-NO', {
    month: '2-digit',
    year: 'numeric',
    timeZone: 'UTC',
  });
}
