import { Component, inject, input, output, signal } from '@angular/core';
import { DateField } from '../date/date-field';
import { messageOf } from '../profile/profile.service';
import { isoDate, parseNumber } from './loan-input';
import { SavedLoan, SavedLoanService } from './saved-loan.service';

/**
 * What net worth needs to know about each saved loan (#135): the balance the bank reports, which
 * counts the loan as a debt from that day, and whether the loan is secured on property, which keeps
 * it out of liquid net worth together with the property.
 */
@Component({
  selector: 'app-loan-records',
  imports: [DateField],
  template: `
    @for (loan of loans(); track loan.id) {
      <div class="loan">
        <strong>{{ loan.name }}</strong>
        <label class="check">
          <input
            type="checkbox"
            [checked]="loan.securedOnProperty"
            [disabled]="busy()"
            (change)="flag(loan, $event)"
          />
          Secured on property
        </label>
        <div class="balance">
          <label class="field">
            <span>Balance ({{ loan.currencyCode }})</span>
            <input
              inputmode="decimal"
              [value]="amounts()[loan.id] ?? ''"
              (input)="setAmount(loan, $event)"
            />
          </label>
          <div class="field">
            <span>On</span>
            <app-date-field
              [label]="'Balance date of ' + loan.name"
              [max]="today"
              [value]="dates()[loan.id] ?? today"
              (valueChange)="setDate(loan, $event)"
            />
          </div>
          <button type="button" [disabled]="busy()" (click)="record(loan)">Record balance</button>
        </div>
      </div>
    }
    @if (message(); as text) {
      <p role="status">{{ text }}</p>
    }
  `,
  styles: `
    .loan {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 10px 0;
      border-top: 1px solid var(--fg-line);
    }
    .check {
      display: flex;
      align-items: center;
      gap: 8px;
      min-height: 44px;
    }
    .balance {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: flex-end;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 13px;
      color: var(--fg-muted);
      flex: 1 1 140px;
    }
    .field > input {
      min-height: 44px;
      padding: 0 10px;
      border-radius: 10px;
      border: 1px solid var(--fg-line);
      background: var(--fg-surface);
      color: var(--fg-ink);
      font: inherit;
      box-sizing: border-box;
      width: 100%;
    }
    button {
      min-height: 44px;
      padding: 0 16px;
      border-radius: 10px;
      font: inherit;
      cursor: pointer;
      border: 1px solid var(--fg-line);
      background: transparent;
      color: var(--fg-ink);
    }
  `,
})
export class LoanRecords {
  private readonly service = inject(SavedLoanService);

  readonly profileId = input.required<number>();
  readonly loans = input.required<readonly SavedLoan[]>();
  /** The loan as it is after the change, so the list above can show it. */
  readonly changed = output<SavedLoan>();

  protected readonly today = isoDate(new Date());
  protected readonly busy = signal(false);
  protected readonly message = signal<string | undefined>(undefined);
  protected readonly amounts = signal<Record<number, string>>({});
  protected readonly dates = signal<Record<number, string>>({});

  protected setAmount(loan: SavedLoan, event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.amounts.update((all) => ({ ...all, [loan.id]: value }));
  }

  protected setDate(loan: SavedLoan, value: string): void {
    this.dates.update((all) => ({ ...all, [loan.id]: value }));
  }

  protected async flag(loan: SavedLoan, event: Event): Promise<void> {
    const box = event.target as HTMLInputElement;
    await this.run(
      async () => {
        const updated = await this.service.setSecured(this.profileId(), loan.id, box.checked);
        this.changed.emit(updated);
        this.message.set(
          `${loan.name} is ${updated.securedOnProperty ? '' : 'not '}secured on property.`,
        );
      },
      () => (box.checked = loan.securedOnProperty),
    );
  }

  protected async record(loan: SavedLoan): Promise<void> {
    const text = (this.amounts()[loan.id] ?? '').trim();
    const amount = parseNumber(text);
    const date = this.dates()[loan.id] ?? this.today;
    if (text === '' || !(amount >= 0)) {
      this.message.set(`${loan.name}: enter the balance as a number, 0 or more.`);
      return;
    }
    if (date === '') {
      this.message.set(`${loan.name}: enter the balance date as dd.mm.yyyy.`);
      return;
    }

    await this.run(async () => {
      await this.service.recordBalance(this.profileId(), loan.id, date, amount);
      this.message.set(`Recorded the balance of ${loan.name}.`);
    });
  }

  private async run(action: () => Promise<void>, undo?: () => void): Promise<void> {
    this.busy.set(true);
    this.message.set(undefined);
    try {
      await action();
    } catch (error) {
      undo?.();
      this.message.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }
}
