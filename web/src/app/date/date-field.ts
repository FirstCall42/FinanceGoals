import { Component, ElementRef, computed, input, model, signal, viewChild } from '@angular/core';

/**
 * A date as dd.MM.yyyy whatever the browser's locale (#117): type it, or pick it with the calendar
 * button (the browser's own picker, whose language we can't change). The value is an ISO date
 * (yyyy-MM-dd), or empty while the text is not a valid date.
 */
@Component({
  selector: 'app-date-field',
  template: `
    <div class="wrap">
      <input
        type="text"
        inputmode="numeric"
        autocomplete="off"
        placeholder="dd.mm.yyyy"
        [attr.aria-label]="label() || null"
        [attr.aria-invalid]="invalid() ? 'true' : null"
        [value]="shown()"
        (input)="type($event)"
        (blur)="leave()"
      />
      <button type="button" class="pick" aria-label="Pick a date" (click)="pick()">
        <svg
          width="18"
          height="18"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          stroke-width="2"
          stroke-linecap="round"
          aria-hidden="true"
        >
          <rect x="3" y="5" width="18" height="16" rx="2"></rect>
          <path d="M3 10h18M8 3v4M16 3v4"></path>
        </svg>
      </button>
      <input
        #native
        class="native"
        type="date"
        tabindex="-1"
        aria-hidden="true"
        [value]="value()"
        [max]="max()"
        (change)="picked($event)"
      />
    </div>
  `,
  styles: `
    .wrap {
      position: relative;
      display: flex;
      gap: 6px;
    }
    input[type='text'] {
      flex: 1;
      min-width: 0;
      min-height: 44px;
      padding: 0 10px;
      border-radius: 10px;
      border: 1px solid var(--fg-line);
      background: var(--fg-surface);
      color: var(--fg-ink);
      font: inherit;
    }
    input[aria-invalid='true'] {
      border-color: var(--fg-negative, currentColor);
    }
    .pick {
      min-width: 44px;
      min-height: 44px;
      display: flex;
      align-items: center;
      justify-content: center;
      border-radius: 10px;
      border: 1px solid var(--fg-line);
      background: var(--fg-surface);
      color: var(--fg-ink);
      cursor: pointer;
    }
    /* The browser's picker opens from this input, which stays under the button. */
    .native {
      position: absolute;
      right: 0;
      bottom: 0;
      width: 44px;
      height: 44px;
      opacity: 0;
      pointer-events: none;
    }
  `,
})
export class DateField {
  /** The ISO date (yyyy-MM-dd), or empty. */
  readonly value = model('');
  /** What the field is for, read out by screen readers. */
  readonly label = input('');
  /** The latest ISO date allowed, if any. */
  readonly max = input<string | undefined>(undefined);

  protected readonly typing = signal<string | undefined>(undefined);
  // What was typed stays while it is a date or not yet one; a date set from outside replaces it.
  protected readonly shown = computed(() => {
    const typed = this.typing();
    return typed !== undefined && parse(typed) === this.value() ? typed : display(this.value());
  });
  protected readonly invalid = computed(() => this.value() === '' && (this.typing() ?? '') !== '');
  private readonly native = viewChild.required<ElementRef<HTMLInputElement>>('native');

  protected type(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.typing.set(text);
    const iso = text.trim() === '' ? '' : parse(text);
    const limit = this.max();
    this.value.set(iso !== '' && limit && iso > limit ? '' : iso);
  }

  /** A valid date is shown in full form; text that is not a date stays so it can be fixed. */
  protected leave(): void {
    if (this.value() !== '') {
      this.typing.set(undefined);
    }
  }

  protected pick(): void {
    const element = this.native().nativeElement;
    try {
      element.showPicker();
    } catch {
      // Without showPicker (older browsers) the typed text is the way in.
      element.focus();
    }
  }

  protected picked(event: Event): void {
    this.typing.set(undefined);
    this.value.set((event.target as HTMLInputElement).value);
  }
}

/** yyyy-MM-dd as dd.MM.yyyy; anything else as it is. */
export function display(iso: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(iso);
  return match ? `${match[3]}.${match[2]}.${match[1]}` : iso;
}

/** A date for display: dd.MM.yyyy. Takes an ISO date, or a timestamp (its UTC date). */
export function formatDate(value: string): string {
  return display(value.length > 10 ? new Date(value).toISOString().slice(0, 10) : value);
}

/** dd.MM.yyyy (also with / or -, and one-digit days and months) as yyyy-MM-dd, or empty if it is not a real date. */
export function parse(text: string): string {
  const match = /^(\d{1,2})[./-](\d{1,2})[./-](\d{4})$/.exec(text.trim());
  if (!match) {
    return '';
  }

  const [day, month, year] = [Number(match[1]), Number(match[2]), Number(match[3])];
  const date = new Date(Date.UTC(year, month - 1, day));
  const real =
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day;
  return real ? date.toISOString().slice(0, 10) : '';
}
