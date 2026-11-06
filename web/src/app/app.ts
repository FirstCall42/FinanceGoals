import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SignIn } from './auth/sign-in';
import { CurrencySwitcher } from './currency/currency-switcher';
import { ThemeService } from './theme/theme.service';

@Component({
  imports: [RouterOutlet, RouterLink, RouterLinkActive, CurrencySwitcher, SignIn],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  protected readonly theme = inject(ThemeService);
}
