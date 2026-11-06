import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../config/app-config';
import { GoogleIdentity, loadGoogleIdentity, tokenExpiry } from './google-identity';

/** The signed-in person. */
export interface User {
  readonly id: number;
  readonly email: string;
  readonly name: string | null;
}

/** A household profile the person belongs to, and their role in it. */
export interface Profile {
  readonly id: number;
  readonly name: string;
  readonly role: 'Owner' | 'Member';
  /** The household's main currency (#105). */
  readonly mainCurrency: string;
}

const storageKey = 'financegoals.idToken';

/**
 * Sign-in with Google (#24). Google Identity Services gives the browser an ID token (valid for an
 * hour); every API request carries it, and the API checks it with Google. The token is kept for
 * this tab only (sessionStorage), so a reload keeps you signed in until it expires.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);
  private google: GoogleIdentity | undefined;
  private starting: Promise<void> | undefined;

  readonly token = signal<string | undefined>(undefined);
  readonly user = signal<User | undefined>(undefined);
  /** The person's profiles; a first sign-in creates one. */
  readonly profiles = signal<readonly Profile[]>([]);
  /** False until the client ID is known; stays false when sign-in is not set up. */
  readonly available = signal(false);
  readonly error = signal<string | undefined>(undefined);
  readonly signedIn = computed(() => this.user() !== undefined);

  /** Loads the sign-in settings and Google's script, and restores this tab's sign-in. Runs once. */
  start(): Promise<void> {
    this.starting ??= this.load();
    return this.starting;
  }

  /** Draws Google's sign-in button into an element. */
  async renderButton(parent: HTMLElement): Promise<void> {
    await this.start();
    this.google?.renderButton(parent, {
      type: 'standard',
      theme: 'outline',
      size: 'large',
      shape: 'pill',
      locale: 'en',
    });
  }

  signOut(): void {
    this.google?.disableAutoSelect();
    this.clear();
  }

  /** Forgets a token the API refused or that has expired. */
  clear(): void {
    this.token.set(undefined);
    this.user.set(undefined);
    this.profiles.set([]);
    try {
      sessionStorage.removeItem(storageKey);
    } catch {
      // Storage can be blocked; there is nothing to remove then.
    }
  }

  /** Signs in with a Google ID token: the API checks it and records the sign-in. */
  async signIn(credential: string): Promise<void> {
    if (tokenExpiry(credential) * 1000 <= Date.now()) {
      this.clear();
      return;
    }

    this.token.set(credential);
    try {
      const signedIn = await this.me();
      this.user.set(signedIn.user);
      this.profiles.set(signedIn.profiles);
      this.error.set(undefined);
      try {
        sessionStorage.setItem(storageKey, credential);
      } catch {
        // Blocked storage: the sign-in lasts until the page is reloaded.
      }
    } catch {
      this.clear();
      this.error.set("Signing in didn't work. Try again.");
    }
  }

  /** Reloads the user and their profiles, e.g. after joining a household. */
  async refresh(): Promise<void> {
    if (this.token()) {
      const signedIn = await this.me();
      this.user.set(signedIn.user);
      this.profiles.set(signedIn.profiles);
    }
  }

  private me(): Promise<{ user: User; profiles: Profile[] }> {
    // On an invite link the person is about to join a household, so the API shouldn't create an
    // empty one of their own.
    const joining = window.location.pathname.startsWith('/invite/');
    const path = joining ? '/api/me?createProfile=false' : '/api/me';
    return firstValueFrom(
      this.http.post<{ user: User; profiles: Profile[] }>(this.config.apiUrl(path), null),
    );
  }

  private async load(): Promise<void> {
    try {
      const settings = await firstValueFrom(
        this.http.get<{ googleClientId: string | null }>(this.config.apiUrl('/api/auth/config')),
      );
      if (!settings.googleClientId) {
        return;
      }

      this.google = await loadGoogleIdentity();
      this.google.initialize({
        client_id: settings.googleClientId,
        callback: (response) => void this.signIn(response.credential),
        auto_select: true,
        use_fedcm_for_prompt: true,
      });
      this.available.set(true);
      const saved = readSaved();
      if (saved) {
        await this.signIn(saved);
      }
    } catch {
      this.error.set("Sign-in isn't available right now.");
    }
  }
}

function readSaved(): string | undefined {
  try {
    return sessionStorage.getItem(storageKey) ?? undefined;
  } catch {
    return undefined;
  }
}
