import { createContext } from 'react';
import type { AuthSnapshot } from './session';
import type { LoginRequest } from '../types/api';

export type AuthContextValue = AuthSnapshot & {
  signIn: (credentials: LoginRequest) => Promise<void>;
  signOut: () => void;
};

export const AuthContext = createContext<AuthContextValue | null>(null);
