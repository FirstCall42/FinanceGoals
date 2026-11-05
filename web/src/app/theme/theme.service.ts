import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export type Theme = 'light' | 'dark';

const storageKey = 'financegoals.theme';

/**
 * Light or dark. Until the user picks one, the system setting decides (styles.scss); a pick is
 * kept in this browser only. Storage can be unavailable (private windows), so every access is guarded.
 */
@Injectable({ providedIn: 'root' })
export class ThemeService {
  private readonly document = inject(DOCUMENT);
  private readonly picked = signal<Theme | null>(this.read());

  constructor() {
    this.apply(this.picked());
  }

  /** The theme in effect: the user's pick, else the system's. */
  current(): Theme {
    return this.picked() ?? (this.systemPrefersDark() ? 'dark' : 'light');
  }

  toggle(): void {
    const next: Theme = this.current() === 'dark' ? 'light' : 'dark';
    this.picked.set(next);
    this.apply(next);
    try {
      this.document.defaultView?.localStorage.setItem(storageKey, next);
    } catch {
      // Not remembered; the theme still applies for this visit.
    }
  }

  private apply(theme: Theme | null): void {
    const root = this.document.documentElement;
    if (theme) {
      root.setAttribute('data-theme', theme);
    } else {
      root.removeAttribute('data-theme');
    }
  }

  private read(): Theme | null {
    try {
      const value = this.document.defaultView?.localStorage.getItem(storageKey);
      return value === 'light' || value === 'dark' ? value : null;
    } catch {
      return null;
    }
  }

  private systemPrefersDark(): boolean {
    return this.document.defaultView?.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
  }
}
