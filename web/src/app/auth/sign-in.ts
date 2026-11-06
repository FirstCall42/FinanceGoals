import { Component, ElementRef, effect, inject, viewChild } from '@angular/core';
import { AuthService } from './auth.service';

/** Google's sign-in button, or the signed-in person with a sign-out button. */
@Component({
  selector: 'app-sign-in',
  template: `
    @if (auth.user(); as user) {
      <span class="who" [title]="user.email">{{ user.name ?? user.email }}</span>
      <button type="button" class="sign-out" (click)="auth.signOut()">Sign out</button>
    } @else {
      <div #button class="google"></div>
      @if (auth.error(); as message) {
        <span class="error" role="status">{{ message }}</span>
      }
    }
  `,
  styles: `
    :host {
      display: flex;
      align-items: center;
      gap: 8px;
      font-size: 14px;
    }
    .who {
      max-width: 16ch;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
    .sign-out {
      min-height: 44px;
      padding: 0 14px;
      border-radius: 10px;
      border: 1px solid var(--fg-line);
      background: var(--fg-surface);
      color: var(--fg-ink);
      font: inherit;
      cursor: pointer;
    }
    .google {
      min-height: 44px;
      display: flex;
      align-items: center;
    }
    .error {
      color: var(--fg-muted);
      font-size: 13px;
    }
  `,
})
export class SignIn {
  protected readonly auth = inject(AuthService);
  private readonly button = viewChild<ElementRef<HTMLElement>>('button');

  constructor() {
    void this.auth.start();
    // Google draws its button into the element whenever it is shown (at start and after sign-out).
    effect(() => {
      const element = this.button()?.nativeElement;
      if (element && this.auth.available()) {
        void this.auth.renderButton(element);
      }
    });
  }
}
