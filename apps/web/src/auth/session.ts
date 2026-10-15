import type { AuthenticatedAgent } from '../types/api';

export const ACCESS_TOKEN_STORAGE_KEY = 'northwind-support.access-token';

export type AuthSnapshot =
  | { status: 'loading'; agent: null }
  | { status: 'anonymous'; agent: null }
  | { status: 'authenticated'; agent: AuthenticatedAgent };

const listeners = new Set<() => void>();
let snapshot: AuthSnapshot = { status: 'loading', agent: null };

export function subscribeToAuth(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

export function getAuthSnapshot(): AuthSnapshot {
  return snapshot;
}

export function getAccessToken(): string | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    return window.sessionStorage.getItem(ACCESS_TOKEN_STORAGE_KEY);
  } catch {
    return null;
  }
}

export function setAuthenticatedSession(accessToken: string, agent: AuthenticatedAgent): void {
  try {
    window.sessionStorage.setItem(ACCESS_TOKEN_STORAGE_KEY, accessToken);
  } catch {
    throw new Error('This browser could not save the sign-in session. Enable session storage and try again.');
  }

  publish({ status: 'authenticated', agent });
}

export function setRestoredAgent(agent: AuthenticatedAgent): void {
  if (getAccessToken()) {
    publish({ status: 'authenticated', agent });
  }
}

export function setAnonymous(): void {
  publish({ status: 'anonymous', agent: null });
}

export function clearAuthSession(): void {
  const hadToken = getAccessToken() !== null;
  try {
    window.sessionStorage.removeItem(ACCESS_TOKEN_STORAGE_KEY);
  } catch {
    // Keep the in-memory session cleared when storage is unavailable.
  }

  if (hadToken || snapshot.status !== 'anonymous') {
    publish({ status: 'anonymous', agent: null });
  }
}

function publish(next: AuthSnapshot): void {
  snapshot = next;
  listeners.forEach((listener) => listener());
}
