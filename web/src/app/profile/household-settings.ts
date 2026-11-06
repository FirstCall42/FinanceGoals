import { Component, computed, effect, inject, signal } from '@angular/core';
import { formatDate } from '../date/date-field';
import { AuthService } from '../auth/auth.service';
import { CreatedInvite, Member, PendingInvite, ProfileService, messageOf } from './profile.service';

/**
 * The household in Settings (#26): its members, and for Owners an invite form and the pending
 * invites. The app sends no email: the Owner copies the link and sends it themselves.
 */
@Component({
  selector: 'app-household-settings',
  template: `
    @if (profile(); as p) {
      <section aria-labelledby="household-title">
        <h2 id="household-title">Household: {{ p.name }}</h2>
        <ul class="members">
          @for (m of members(); track m.userId) {
            <li>
              <span>{{ m.name ?? m.email }}</span>
              <small>{{ m.email }} · {{ m.role }}</small>
            </li>
          }
        </ul>

        @if (p.role === 'Owner') {
          <form class="invite" (submit)="invite($event)">
            <label class="field">
              <span>Invite someone by their Google account's email</span>
              <input
                type="email"
                autocomplete="off"
                [value]="email()"
                (input)="email.set(text($event))"
              />
            </label>
            <button type="submit" class="primary" [disabled]="busy()">Create invite link</button>
          </form>

          @if (created(); as c) {
            <div class="link" role="status">
              <p>
                Send this link to <strong>{{ c.email }}</strong
                >. It works once, only for that Google account, until {{ date(c.expiresAt) }}.
              </p>
              <input readonly [value]="link(c.token)" aria-label="Invite link" />
              <button type="button" class="secondary" (click)="copy(c.token)">
                {{ copied() ? 'Copied' : 'Copy link' }}
              </button>
            </div>
          }

          @if (pending().length > 0) {
            <h3>Waiting to join</h3>
            <ul class="members">
              @for (i of pending(); track i.id) {
                <li>
                  <span>{{ i.email }}</span>
                  <small>until {{ date(i.expiresAt) }}</small>
                  <button type="button" class="secondary" (click)="revoke(i.id)">Withdraw</button>
                </li>
              }
            </ul>
          }
        }

        @if (error(); as message) {
          <p class="error" role="alert">{{ message }}</p>
        }
      </section>
    }
  `,
  styles: `
    section {
      background: var(--fg-surface);
      border: 1px solid var(--fg-line);
      border-radius: var(--fg-radius);
      padding: 20px;
      display: flex;
      flex-direction: column;
      gap: 14px;
      max-width: 560px;
    }
    h2 {
      margin: 0;
      font-size: 18px;
    }
    h3 {
      margin: 0;
      font-size: 15px;
    }
    p {
      margin: 0;
    }
    .members {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .members li {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: 4px 12px;
    }
    small {
      color: var(--fg-muted);
    }
    .invite,
    .link {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }
    .field {
      display: flex;
      flex-direction: column;
      gap: 4px;
      font-size: 13px;
      color: var(--fg-muted);
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
      align-self: flex-start;
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
      padding: 10px 12px;
      border-radius: 10px;
      background: var(--fg-warn-bg);
      color: var(--fg-warn-ink);
      border: 1px solid var(--fg-warn-line);
    }
  `,
})
export class HouseholdSettings {
  private readonly auth = inject(AuthService);
  private readonly profiles = inject(ProfileService);

  /** The household; one per person for now. */
  protected readonly profile = computed(() => this.auth.profiles()[0]);
  protected readonly members = signal<Member[]>([]);
  protected readonly pending = signal<PendingInvite[]>([]);
  protected readonly created = signal<CreatedInvite | undefined>(undefined);
  protected readonly email = signal('');
  protected readonly busy = signal(false);
  protected readonly copied = signal(false);
  protected readonly error = signal<string | undefined>(undefined);

  constructor() {
    effect(() => {
      const p = this.profile();
      if (p) {
        void this.load(p.id, p.role === 'Owner');
      }
    });
  }

  protected text(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected link(token: string): string {
    return this.profiles.link(token);
  }

  protected date(iso: string): string {
    return formatDate(iso);
  }

  protected async invite(event: Event): Promise<void> {
    event.preventDefault();
    const p = this.profile();
    if (!p) {
      return;
    }

    this.busy.set(true);
    this.error.set(undefined);
    try {
      this.created.set(await this.profiles.invite(p.id, this.email().trim()));
      this.copied.set(false);
      this.email.set('');
      this.pending.set(await this.profiles.invites(p.id));
    } catch (error) {
      this.error.set(messageOf(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async copy(token: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.link(token));
      this.copied.set(true);
    } catch {
      this.error.set('Copying was blocked; select the link and copy it.');
    }
  }

  protected async revoke(inviteId: number): Promise<void> {
    const p = this.profile();
    if (!p) {
      return;
    }

    try {
      await this.profiles.revoke(p.id, inviteId);
      this.pending.set(await this.profiles.invites(p.id));
      if (this.created()?.id === inviteId) {
        this.created.set(undefined);
      }
    } catch (error) {
      this.error.set(messageOf(error));
    }
  }

  private async load(profileId: number, owner: boolean): Promise<void> {
    try {
      this.members.set(await this.profiles.members(profileId));
      this.pending.set(owner ? await this.profiles.invites(profileId) : []);
    } catch (error) {
      this.error.set(messageOf(error));
    }
  }
}
