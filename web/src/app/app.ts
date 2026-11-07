import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map } from 'rxjs';
import { AuthService } from './auth/auth.service';
import { SignIn } from './auth/sign-in';
import { CurrencySwitcher } from './currency/currency-switcher';
import { ThemeService } from './theme/theme.service';

/** Pages that work without signing in (the health overview and the calculator); the rest ask for a sign-in. */
const publicPaths = ['/', '/calculator'];

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CurrencySwitcher, SignIn],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  protected readonly theme = inject(ThemeService);
  protected readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => this.router.url.split(/[?#]/)[0]),
    ),
    { initialValue: this.router.url.split(/[?#]/)[0] },
  );
  /** The app itself, or the login page instead: signed in, or on a page that needs no account. */
  protected readonly showApp = computed(
    () => this.auth.signedIn() || publicPaths.includes(this.path()),
  );
}
