import { useCallback, useEffect, useMemo, useSyncExternalStore } from 'react';
import type { ReactNode } from 'react';
import type { AuthenticatedAgent, LoginRequest } from '../types/api';
import { authApi } from '../api/auth';
import { AuthContext } from './context';
import {
  clearAuthSession,
  getAccessToken,
  getAuthSnapshot,
  setAnonymous,
  setAuthenticatedSession,
  setRestoredAgent,
  subscribeToAuth,
} from './session';

let restorationRequest: Promise<AuthenticatedAgent> | null = null;

function restoreCurrentAgent(): Promise<AuthenticatedAgent> {
  restorationRequest ??= authApi.getCurrentAgent().finally(() => {
    restorationRequest = null;
  });
  return restorationRequest;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const snapshot = useSyncExternalStore(subscribeToAuth, getAuthSnapshot, getAuthSnapshot);

  useEffect(() => {
    let active = true;
    const token = getAccessToken();
    if (!token) {
      setAnonymous();
      return () => {
        active = false;
      };
    }

    void restoreCurrentAgent()
      .then((agent) => {
        if (active && getAccessToken() === token) {
          setRestoredAgent(agent);
        }
      })
      .catch(() => {
        if (getAccessToken() === token) {
          clearAuthSession();
        }
      });

    return () => {
      active = false;
    };
  }, []);

  const signIn = useCallback(async (credentials: LoginRequest) => {
    const response = await authApi.login(credentials);
    setAuthenticatedSession(response.accessToken, response.agent);
  }, []);

  const signOut = useCallback(() => clearAuthSession(), []);
  const value = useMemo(() => ({ ...snapshot, signIn, signOut }), [snapshot, signIn, signOut]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
