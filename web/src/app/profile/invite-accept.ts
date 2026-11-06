import { Component, inject, input, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { ProfileService } from './profile.service';

/** /invite/:token: joins the household the link was sent from (#26). */
@Component({
  selector: 'app-invite-accept',
  imports: [RouterLink],
  template: `
    <section class="panel" aria-labelledby="invite-title">
      <h1 id="invite-title">Join a household</h1>
      @if (joined()) {
        <p role="status">You've joined the household. Welcome!</p>
        <a routerLink="/">Go to the overview</a>
      } @else if (auth.signedIn()) {
        <p>
          You're signed in as <strong>{{ auth.user()?.email }}</strong
          >. The invite works only for the Google account it was sent to.
        </p>
        <button type="button" class="primary" [disabled]="busy()" (click)="accept()">
          {{ busy() ? 'Joining…' : 'Join household' }}
        </button>
      } @else {
        <p>
          Sign in with Google (top right) using the account the invite was sent to, then join the
          household here.
        </p>
      }
      @if (error(); as message) {
        <p class="error" role="alert">{{ message }}</p>
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
      align-items: flex-start;
      gap: 12px;
      max-width: 560px;
    }
    h1 {
      margin: 0;
      font-size: 20px;
    }
    p {
      margin: 0;
    }
    .primary {
      min-height: 44px;
      padding: 0 16px;
      border-radius: 10px;
      border: 0;
      background: var(--fg-accent);
      color: var(--fg-on-accent);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
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
export class InviteAccept {
  protected readonly auth = inject(AuthService);
  private readonly profiles = inject(ProfileService);

  /** From the route. */
  readonly token = input.required<string>();

  protected readonly busy = signal(false);
  protected readonly joined = signal(false);
  protected readonly error = signal<string | undefined>(undefined);

  protected async accept(): Promise<void> {
    this.busy.set(true);
    this.error.set(undefined);
    try {
      await this.profiles.accept(this.token());
      await this.auth.refresh();
      this.joined.set(true);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : String(error));
    } finally {
      this.busy.set(false);
    }
  }
}
