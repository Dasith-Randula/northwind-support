import type { AuthenticatedAgent, LoginRequest, LoginResponse } from '../types/api';
import { request } from './client';

export const authApi = {
  login: (credentials: LoginRequest) =>
    request<LoginResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify(credentials),
    }),

  getCurrentAgent: () => request<AuthenticatedAgent>('/auth/me'),
};
