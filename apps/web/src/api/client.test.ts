import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError, request } from './client';
import {
  ACCESS_TOKEN_STORAGE_KEY,
  clearAuthSession,
  getAuthSnapshot,
  setAuthenticatedSession,
} from '../auth/session';

const agent = { id: 7, fullName: 'Alex Turner', email: 'alex@example.test' };

describe('central API authentication', () => {
  beforeEach(() => {
    sessionStorage.clear();
    clearAuthSession();
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => vi.unstubAllGlobals());

  it('attaches a stored bearer token to protected API calls', async () => {
    sessionStorage.setItem(ACCESS_TOKEN_STORAGE_KEY, 'protected-jwt');
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));

    await expect(request<{ ok: boolean }>('/tickets')).resolves.toEqual({ ok: true });
    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer protected-jwt');
  });

  it('does not send an authorization header when there is no token', async () => {
    vi.mocked(fetch).mockResolvedValue(new Response(JSON.stringify({ ok: true }), { status: 200 }));

    await request('/tickets', { headers: { Authorization: 'Bearer stale-token' } });
    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(new Headers(init?.headers).has('Authorization')).toBe(false);
  });

  it('invalidates the session centrally after a protected request returns 401', async () => {
    setAuthenticatedSession('expired-jwt', agent);
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401, statusText: 'Unauthorized' }),
    );

    await expect(request('/tickets')).rejects.toBeInstanceOf(ApiError);
    expect(sessionStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)).toBeNull();
    expect(getAuthSnapshot()).toEqual({ status: 'anonymous', agent: null });
  });

  it('keeps login requests unauthenticated and leaves their 401 to the login form', async () => {
    setAuthenticatedSession('existing-jwt', agent);
    vi.mocked(fetch).mockResolvedValue(
      new Response(JSON.stringify({ title: 'Invalid credentials' }), { status: 401, statusText: 'Unauthorized' }),
    );

    await expect(request('/auth/login', { method: 'POST', body: '{}' })).rejects.toBeInstanceOf(ApiError);
    const [, init] = vi.mocked(fetch).mock.calls[0];
    expect(new Headers(init?.headers).has('Authorization')).toBe(false);
    expect(sessionStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)).toBe('existing-jwt');
    expect(getAuthSnapshot().status).toBe('authenticated');
  });
});
