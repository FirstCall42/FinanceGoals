import { Component, computed, input } from '@angular/core';

/**
 * Shown instead of an amount when an exchange rate is missing. It never shows a guessed amount:
 * a missing rate is never treated as 1.0 (CLAUDE.md, Money).
 */
@Component({
  selector: 'app-missing-rate',
  template: `
    <div role="status" class="warning">
      <svg
        width="18"
        height="18"
        viewBox="0 0 24 24"
        fill="none"
        stroke="currentColor"
        stroke-width="2"
        stroke-linecap="round"
        stroke-linejoin="round"
        aria-hidden="true"
      >
        <path d="M12 3l9 16H3z"></path>
        <path d="M12 10v4"></path>
        <path d="M12 17h.01"></path>
      </svg>
      <span>{{ message() }}</span>
    </div>
  `,
  styles: `
    .warning {
      display: flex;
      gap: 10px;
      align-items: flex-start;
      background: var(--fg-warn-bg);
      color: var(--fg-warn-ink);
      border: 1px solid var(--fg-warn-line);
      border-radius: 12px;
      padding: 12px 14px;
      font-size: 14px;
    }
    svg {
      flex: none;
      margin-top: 2px;
    }
  `,
})
export class MissingRate {
  /** The currencies without a rate, e.g. ['RON']. */
  readonly currencyCodes = input.required<readonly string[]>();
  /** The day the conversion was for, as yyyy-MM-dd. */
  readonly date = input.required<string>();

  protected readonly message = computed(
    () =>
      `No exchange rate for ${this.currencyCodes().join(' and ')} on or before ${this.date()}, so this amount can't be shown.`,
  );
}
