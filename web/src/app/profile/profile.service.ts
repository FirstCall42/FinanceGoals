import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AppConfig } from '../config/app-config';

export interface Member {
  readonly userId: number;
  readonly email: string;
  readonly name: string | null;
  readonly role: 'Owner' | 'Member';
}

export interface PendingInvite {
  readonly id: number;
  readonly email: string;
  readonly expiresAt: string;
}

export interface CreatedInvite extends PendingInvite {
  /** Goes in the link; the API shows it only once. */
  readonly token: string;
}

/** Household profile calls: members and invitations (#25, #26). */
@Injectable({ providedIn: 'root' })
export class ProfileService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  members(profileId: number): Promise<Member[]> {
    return firstValueFrom(this.http.get<Member[]>(this.url(profileId, '/members')));
  }

  invites(profileId: number): Promise<PendingInvite[]> {
    return firstValueFrom(this.http.get<PendingInvite[]>(this.url(profileId, '/invites')));
  }

  /** Changes the household's main currency (Owners only). */
  setMainCurrency(profileId: number, mainCurrency: string): Promise<unknown> {
    return firstValueFrom(
      this.http.patch(this.config.apiUrl(`/api/profiles/${profileId}`), { mainCurrency }),
    );
  }

  invite(profileId: number, email: string): Promise<CreatedInvite> {
    return firstValueFrom(
      this.http.post<CreatedInvite>(this.url(profileId, '/invites'), { email }),
    );
  }

  revoke(profileId: number, inviteId: number): Promise<unknown> {
    return firstValueFrom(this.http.delete(this.url(profileId, `/invites/${inviteId}`)));
  }

  /** Joins the household of an invite link; resolves with the profile, or rejects with a message. */
  async accept(token: string): Promise<number> {
    try {
      const accepted = await firstValueFrom(
        this.http.post<{ profileId: number }>(this.config.apiUrl('/api/invites/accept'), {
          token,
        }),
      );
      return accepted.profileId;
    } catch (error) {
      throw new Error(messageOf(error), { cause: error });
    }
  }

  /** The link to send: this app's address with the token. */
  link(token: string): string {
    return `${window.location.origin}/invite/${encodeURIComponent(token)}`;
  }

  private url(profileId: number, path: string): string {
    return this.config.apiUrl(`/api/profiles/${profileId}${path}`);
  }
}

/** The API's own message for a refused request, or a general one. */
export function messageOf(error: unknown): string {
  if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object') {
    const problem = error.error as {
      title?: string;
      detail?: string;
      errors?: Record<string, string[]>;
    };
    const first = problem.errors ? Object.values(problem.errors).flat()[0] : undefined;
    const message = first ?? problem.detail ?? problem.title;
    if (message) {
      return message;
    }
  }

  return "That didn't work right now. Try again in a minute.";
}
