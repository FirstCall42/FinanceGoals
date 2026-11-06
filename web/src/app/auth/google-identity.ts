/** The parts of Google Identity Services (https://accounts.google.com/gsi/client) the app uses. */
export interface GoogleIdentity {
  initialize(options: {
    client_id: string;
    callback: (response: { credential: string }) => void;
    auto_select?: boolean;
    use_fedcm_for_prompt?: boolean;
  }): void;
  renderButton(
    parent: HTMLElement,
    options: {
      type?: 'standard' | 'icon';
      theme?: string;
      size?: string;
      text?: string;
      shape?: string;
      locale?: string;
    },
  ): void;
  disableAutoSelect(): void;
}

declare global {
  interface Window {
    google?: { accounts: { id: GoogleIdentity } };
  }
}

const scriptUrl = 'https://accounts.google.com/gsi/client';
let loading: Promise<GoogleIdentity> | undefined;

/** Loads Google's sign-in script once. */
export function loadGoogleIdentity(): Promise<GoogleIdentity> {
  loading ??= new Promise<GoogleIdentity>((resolve, reject) => {
    if (window.google?.accounts?.id) {
      resolve(window.google.accounts.id);
      return;
    }
    const script = document.createElement('script');
    script.src = scriptUrl;
    script.async = true;
    script.onload = () =>
      window.google?.accounts?.id
        ? resolve(window.google.accounts.id)
        : reject(new Error('Google sign-in did not load.'));
    script.onerror = () => {
      loading = undefined;
      reject(new Error('Google sign-in could not be loaded.'));
    };
    document.head.appendChild(script);
  });
  return loading;
}

/** Seconds since 1970 when a Google ID token expires, or 0 if it can't be read. */
export function tokenExpiry(token: string): number {
  try {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const exp = (JSON.parse(atob(payload)) as { exp?: unknown }).exp;
    return typeof exp === 'number' ? exp : 0;
  } catch {
    return 0;
  }
}
