import { Component, computed, effect, inject, signal } from '@angular/core';
import { AuthService } from '../auth/auth.service';
import { CurrencyService } from '../currency/currency.service';
import { DisplayCurrencyService } from '../currency/display-currency.service';
import { messageOf } from '../profile/profile.service';
import { Account, AccountService, AccountType, accountTypes } from './account.service';

interface Draft {
  name: string;
  type: AccountType;
  currencyCode: string;
}

/**
 * The household's accounts (#35): create, edit and archive. Archived accounts are hidden unless
 * asked for, and can be restored. Values come with the snapshots of #36.
 */
@Component({
  selector: 'app-accounts',
  template: `
    <section class="panel" aria-labelledby="accounts-title">
      <h1 id="accounts-title">Accounts</h1>
      @if (!profile()) {
        <p>Sign in to see and manage your household's accounts.</p>
      } @else {
        <p class="hint">
          Bank, savings, investment, pension and property accounts, each in one currency. Balances
          are added as snapshots later. Archiving hides an account but keeps it, and its name stays
          taken.
        </p>

        <form class="add" (submit)="add($event)">
          <label class="field">
            <span>Name</span>
            <input [value]="draft().name" (input)="setDraft('name', text($event))" />
          </label>
          <label class="field">
            <span>Type</span>
            <select (change)="setDraft('type', $any(text($event)))">
              @for (t of types; track t.value) {
                <option [value]="t.value" [selected]="t.value === draft().type">
                  {{ t.label }}
                </option>
              }
            </select>
          </label>
          <label class="field">
            <span>Currency</span>
            <select (change)="setDraft('currencyCode', text($event))">
              @for (c of currencyCodes(); track c) {
                <option [value]="c" [selected]="c === draft().currencyCode">{{ c }}</option>
              }
            </select>
          </label>
          <button type="submit" class="primary" [disabled]="busy()">Add account</button>
        </form>

        @if (error(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }

        <label class="check">
          <input
            type="checkbox"
            [checked]="showArchived()"
            (change)="showArchived.set(checked($event))"
          />
          Show archived accounts
        </label>

        @if (accounts().length === 0) {
          <p class="empty">No accounts yet. Add your first one above.</p>
        } @else {
          <ul class="list">
            @for (a of accounts(); track a.id) {
              <li [class.archived]="a.archived">
                @if (editing() === a.id) {
                  <div class="row">
                    <label class="field">
                      <span>Name</span>
                      <input [value]="edit().name" (input)="setEdit('name', text($event))" />
                    </label>
                    <label class="field">
                      <span>Type</span>
                      <select (change)="setEdit('type', $any(text($event)))">
                        @for (t of types; track t.value) {
                          <option [value]="t.value" [selected]="t.value === edit().type">
                            {{ t.label }}
                          </option>
                        }
                      </select>
                    </label>
                    <label class="field">
                      <span>Currency</span>
                      <select (change)="setEdit('currencyCode', text($event))">
                        @for (c of currencyCodes(); track c) {
                          <option [value]="c" [selected]="c === edit().currencyCode">
                            {{ c }}
                          </option>
                        }
                      </select>
                    </label>
                    <button type="button" class="primary" (click)="saveEdit(a)">Save</button>
                    <button type="button" class="secondary" (click)="editing.set(undefined)">
                      Cancel
                    </button>
                  </div>
                } @else {
                  <div class="row">
                    <span class="name">{{ a.name }}</span>
                    <span class="meta">{{ typeLabel(a.type) }} · {{ a.currencyCode }}</span>
                    @if (a.archived) {
                      <span class="badge">Archived</span>
                    }
                    <span class="spacer"></span>
                    <button type="button" class="secondary" (click)="startEdit(a)">Edit</button>
                    <button type="button" class="secondary" (click)="archive(a, !a.archived)">
                      {{ a.archived ? 'Restore' : 'Archive' }}
                    </button>
                  </div>
                }
              </li>
            }
          </ul>
        }
      }
    </section>
  `,
  styles: `
    .panel {
      background: var(--fg-surface);
      border: 1px solid var(--fg-line);
      border-radius: var(--fg-radius);
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 16px;
      max-width: 860px;
    }
    h1 {
      margin: 0;
      font-size: 20px;
    }
    p {
      margin: 0;
    }
    .hint,
    .empty,
    .meta {
      color: var(--fg-muted);
    }
    .add,
    .row {
      display: flex;
      flex-wrap: wrap;
      gap: 12px;
      align-items: flex-end;
    }
    .row {
      align-items: center;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 13px;
      color: var(--fg-muted);
      flex: 1 1 160px;
    }
    input:not([type='checkbox']),
    select {
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
    .check {
      display: flex;
      align-items: center;
      gap: 8px;
      color: var(--fg-muted);
    }
    .list {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
    }
    .list li {
      padding: 10px 0;
      border-top: 1px solid var(--fg-line);
    }
    .list li.archived .name {
      color: var(--fg-muted);
    }
    .name {
      font-weight: 600;
    }
    .spacer {
      flex: 1 1 0;
    }
    .badge {
      font-size: 12px;
      padding: 2px 8px;
      border-radius: 999px;
      border: 1px solid var(--fg-line);
      color: var(--fg-muted);
    }
    .error {
      padding: 10px 12px;
      border-radius: 10px;
      background: var(--fg-warn-bg);
      color: var(--fg-warn-ink);
      border: 1px solid var(--fg-warn-line);
    }
  `,
})
export class Accounts {
  private readonly auth = inject(AuthService);
  private readonly service = inject(AccountService);
  private readonly currencies = inject(CurrencyService);
  private readonly display = inject(DisplayCurrencyService);

  protected readonly types = accountTypes;
  protected readonly profile = computed(() => this.auth.profiles()[0]);
  protected readonly accounts = signal<readonly Account[]>([]);
  protected readonly showArchived = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | undefined>(undefined);
  protected readonly draft = signal<Draft>({ name: '', type: 'Current', currencyCode: 'NOK' });
  protected readonly editing = signal<number | undefined>(undefined);
  protected readonly edit = signal<Draft>({ name: '', type: 'Current', currencyCode: 'NOK' });
  protected readonly currencyCodes = computed(() => {
    const codes = this.currencies.currencies().map((c) => c.code);
    return codes.length > 0 ? codes : [this.display.main()];
  });

  constructor() {
    void this.currencies.load();
    effect(() => {
      // The household's main currency is the natural default for a new account.
      this.draft.update((d) => ({ ...d, currencyCode: this.display.main() }));
    });
    effect(() => {
      const p = this.profile();
      if (p) {
        void this.load(p.id, this.showArchived());
      } else {
        this.accounts.set([]);
      }
    });
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement | HTMLSelectElement).value;
  }

  protected checked(event: Event): boolean {
    return (event.target as HTMLInputElement).checked;
  }

  protected typeLabel(type: AccountType): string {
    return this.types.find((t) => t.value === type)?.label ?? type;
  }

  protected setDraft<K extends keyof Draft>(key: K, value: Draft[K]): void {
    this.draft.update((d) => ({ ...d, [key]: value }));
  }

  protected setEdit<K extends keyof Draft>(key: K, value: Draft[K]): void {
    this.edit.update((d) => ({ ...d, [key]: value }));
  }

  protected async add(event: Event): Promise<void> {
    event.preventDefault();
    const p = this.profile();
    if (!p) {
      return;
    }

    const d = this.draft();
    await this.run(async () => {
      await this.service.create(p.id, d.name.trim(), d.type, d.currencyCode);
      this.draft.update((current) => ({ ...current, name: '' }));
      await this.load(p.id, this.showArchived());
    });
  }

  protected startEdit(account: Account): void {
    this.edit.set({ name: account.name, type: account.type, currencyCode: account.currencyCode });
    this.editing.set(Number(account.id));
  }

  protected async saveEdit(account: Account): Promise<void> {
    const p = this.profile();
    if (!p) {
      return;
    }

    const e = this.edit();
    await this.run(async () => {
      await this.service.update(p.id, Number(account.id), {
        name: e.name.trim(),
        type: e.type,
        currencyCode: e.currencyCode,
      });
      this.editing.set(undefined);
      await this.load(p.id, this.showArchived());
    });
  }

  protected async archive(account: Account, archived: boolean): Promise<void> {
    const p = this.profile();
    if (!p) {
      return;
    }

    await this.run(async () => {
      await this.service.update(p.id, Number(account.id), { archived });
      await this.load(p.id, this.showArchived());
    });
  }

  private async load(profileId: number, includeArchived: boolean): Promise<void> {
    try {
      this.accounts.set(await this.service.list(profileId, includeArchived));
    } catch (error) {
      this.error.set(messageOf(error));
    }
  }

  private async run(action: () => Promise<void>): Promise<void> {
    this.busy.set(true);
    this.error.set(undefined);
    try {
      await action();
    } catch (error) {
      this.error.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }
}
