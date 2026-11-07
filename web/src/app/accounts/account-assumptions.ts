import { Component, effect, inject, input, signal } from '@angular/core';
import { parseNumber } from '../loans/loan-input';
import { messageOf } from '../profile/profile.service';
import { Account } from './account.service';
import { Assumption, AssumptionService } from './assumption.service';

interface Draft {
  returnPercent: string;
  contribution: string;
}

/**
 * What each account is expected to earn and what is put into it every month (#152). The forecast
 * (#143) grows each account from its latest balance with these; an account with neither stays flat.
 */
@Component({
  selector: 'app-account-assumptions',
  template: `
    <h2>Expected growth</h2>
    <p class="hint">
      A guess for each account: the yearly return in percent and what you put in each month, in the
      account's own currency. The forecast uses them; leave both empty for a flat account.
    </p>
    @if (message(); as text) {
      <p class="error" role="alert">{{ text }}</p>
    }
    <ul class="list">
      @for (a of accounts(); track a.id) {
        <li>
          <span class="name">{{ a.name }}</span>
          <label class="field">
            <span>Return per year (%)</span>
            <input
              inputmode="decimal"
              [value]="draft(a).returnPercent"
              (input)="set(a, 'returnPercent', text($event))"
            />
          </label>
          <label class="field">
            <span>Per month ({{ a.currencyCode }})</span>
            <input
              inputmode="decimal"
              [value]="draft(a).contribution"
              (input)="set(a, 'contribution', text($event))"
            />
          </label>
          <button type="button" class="primary" [disabled]="busy()" (click)="save(a)">Save</button>
          <button type="button" class="secondary" [disabled]="busy()" (click)="clear(a)">
            Clear
          </button>
        </li>
      }
    </ul>
  `,
  styles: `
    h2 {
      margin: 0;
      font-size: 17px;
    }
    .hint {
      margin: 0;
      color: var(--fg-muted);
    }
    .list {
      list-style: none;
      margin: 0;
      padding: 0;
    }
    li {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: flex-end;
      padding: 10px 0;
      border-top: 1px solid var(--fg-line);
    }
    .name {
      flex: 1 1 100%;
      font-weight: 600;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 13px;
      color: var(--fg-muted);
      flex: 1 1 140px;
    }
    input {
      min-height: 44px;
      padding: 0 10px;
      border-radius: 10px;
      border: 1px solid var(--fg-line);
      background: var(--fg-surface);
      color: var(--fg-ink);
      font: inherit;
      font-size: 15px;
      box-sizing: border-box;
      width: 100%;
    }
    button {
      min-height: 44px;
      padding: 0 16px;
      border-radius: 10px;
      font: inherit;
      cursor: pointer;
    }
    .primary {
      border: 0;
      background: var(--fg-accent);
      color: var(--fg-on-accent);
      font-weight: 600;
    }
    .secondary {
      border: 1px solid var(--fg-line);
      background: transparent;
      color: var(--fg-ink);
    }
    .error {
      margin: 0;
      padding: 10px 12px;
      border-radius: 10px;
      background: var(--fg-warn-bg);
      color: var(--fg-warn-ink);
      border: 1px solid var(--fg-warn-line);
    }
  `,
})
export class AccountAssumptions {
  private readonly service = inject(AssumptionService);

  readonly profileId = input.required<number>();
  readonly accounts = input.required<readonly Account[]>();

  protected readonly busy = signal(false);
  protected readonly message = signal<string | undefined>(undefined);
  private readonly saved = signal<readonly Assumption[]>([]);
  private readonly edits = signal<Record<number, Draft>>({});

  constructor() {
    effect(() => {
      const id = this.profileId();
      void this.load(id);
    });
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  /** What the row shows: what was typed, else what is saved. */
  protected draft(account: Account): Draft {
    const typed = this.edits()[account.id];
    if (typed) {
      return typed;
    }
    const saved = this.saved().find((a) => a.accountId === account.id);
    return {
      returnPercent:
        saved?.returnPercent == null ? '' : String(saved.returnPercent).replace('.', ','),
      contribution:
        saved?.monthlyContribution == null
          ? ''
          : String(saved.monthlyContribution).replace('.', ','),
    };
  }

  protected set(account: Account, key: keyof Draft, value: string): void {
    this.edits.update((all) => ({
      ...all,
      [account.id]: { ...this.draft(account), [key]: value },
    }));
  }

  protected async save(account: Account): Promise<void> {
    const draft = this.draft(account);
    const returnPercent =
      draft.returnPercent.trim() === '' ? null : parseNumber(draft.returnPercent);
    const contribution = draft.contribution.trim() === '' ? null : parseNumber(draft.contribution);
    if (Number.isNaN(returnPercent) || Number.isNaN(contribution)) {
      this.message.set(`${account.name}: enter the return and the contribution as numbers.`);
      return;
    }
    if (returnPercent === null && contribution === null) {
      await this.clear(account);
      return;
    }

    await this.run(async () => {
      await this.service.save(this.profileId(), account.id, returnPercent, contribution);
      await this.load(this.profileId());
      this.forget(account);
    });
  }

  protected async clear(account: Account): Promise<void> {
    await this.run(async () => {
      await this.service.clear(this.profileId(), account.id).catch((error: unknown) => {
        // Nothing saved for the account is as good as cleared.
        if ((error as { status?: number }).status !== 404) {
          throw error;
        }
      });
      await this.load(this.profileId());
      this.forget(account);
    });
  }

  private forget(account: Account): void {
    this.edits.update((all) => {
      const rest = { ...all };
      delete rest[account.id];
      return rest;
    });
  }

  private async load(profileId: number): Promise<void> {
    try {
      this.saved.set(await this.service.list(profileId));
    } catch (error) {
      this.message.set(messageOf(error));
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.message.set(undefined);
    try {
      await action();
    } catch (error) {
      this.message.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }
}
