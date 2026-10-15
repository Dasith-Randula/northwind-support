import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { App } from '../App';
import { ApiError, request } from '../api/client';
import { LoginPage } from '../features/auth/LoginPage';
import { AuthProvider } from './AuthContext';
import { RequireAuth } from './RequireAuth';
import { ACCESS_TOKEN_STORAGE_KEY, clearAuthSession, setAuthenticatedSession } from './session';

const mocks = vi.hoisted(() => ({ login: vi.fn(), getCurrentAgent: vi.fn() }));

vi.mock('../api/auth', () => ({ authApi: mocks }));

const agent = { id: 7, fullName: 'Alex Turner', email: 'alex.turner@example.test' };

function CurrentLocation() {
  const location = useLocation();
  return <output data-testid="current-location">{location.pathname}{location.search}</output>;
}

function TriggerProtectedRequest() {
  return (
    <button type="button" onClick={() => void request('/tickets').catch(() => undefined)}>
      Load protected data
    </button>
  );
}

function renderAuthApp(initialEntry = '/tickets') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <AuthProvider>
        <Routes>
          <Route path="/login" element={<LoginPage />} />
          <Route element={<RequireAuth />}>
            <Route path="/" element={<App />}>
              <Route path="tickets" element={<p>Ticket list page</p>} />
              <Route path="tickets/:id" element={<><p>Ticket detail page</p><CurrentLocation /></>} />
            </Route>
          </Route>
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  );
}

describe('frontend authentication flow', () => {
  afterEach(() => vi.unstubAllGlobals());

  beforeEach(() => {
    sessionStorage.clear();
    clearAuthSession();
    vi.clearAllMocks();
    mocks.getCurrentAgent.mockResolvedValue(agent);
  });

  it('validates required fields and email format without calling login', async () => {
    const user = userEvent.setup();
    renderAuthApp('/login');

    const email = await screen.findByRole('textbox', { name: 'Email' });
    expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Enter your email address.')).toBeInTheDocument();
    expect(screen.getByText('Enter your password.')).toBeInTheDocument();

    await user.type(email, 'not-an-email');
    await user.type(screen.getByLabelText('Password'), 'password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument();
    expect(mocks.login).not.toHaveBeenCalled();
  });

  it('shows invalid-credential errors without clearing or redirecting the login page', async () => {
    const user = userEvent.setup();
    mocks.login.mockRejectedValue(new ApiError(401, 'Email or password is incorrect.'));
    renderAuthApp('/login');

    await user.type(await screen.findByRole('textbox', { name: 'Email' }), 'alex@example.test');
    await user.type(screen.getByLabelText('Password'), 'wrong-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.');
    expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(sessionStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)).toBeNull();
  });

  it('prevents duplicate submissions while login is pending', async () => {
    const user = userEvent.setup();
    let resolveLogin: (result: { accessToken: string; expiresAtUtc: string; agent: typeof agent }) => void =
      () => undefined;
    mocks.login.mockReturnValue(new Promise((resolve) => { resolveLogin = resolve; }));
    renderAuthApp('/login');

    await user.type(await screen.findByRole('textbox', { name: 'Email' }), 'alex@example.test');
    await user.type(screen.getByLabelText('Password'), 'correct-password');
    const submit = screen.getByRole('button', { name: 'Sign in' });
    await user.click(submit);
    expect(await screen.findByRole('button', { name: 'Signing in…' })).toBeDisabled();
    expect(mocks.login).toHaveBeenCalledOnce();

    resolveLogin({ accessToken: 'test-jwt', expiresAtUtc: '2030-01-01T00:00:00Z', agent });
    expect(await screen.findByText('Ticket list page')).toBeInTheDocument();
  });

  it('stores a successful session and returns to the original route with its query', async () => {
    const user = userEvent.setup();
    mocks.login.mockResolvedValue({
      accessToken: 'test-jwt',
      expiresAtUtc: '2030-01-01T00:00:00Z',
      agent,
    });
    renderAuthApp('/tickets/42?tab=history');

    await user.type(await screen.findByRole('textbox', { name: 'Email' }), agent.email);
    await user.type(screen.getByLabelText('Password'), 'correct-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Ticket detail page')).toBeInTheDocument();
    expect(screen.getByTestId('current-location')).toHaveTextContent('/tickets/42?tab=history');
    expect(mocks.login).toHaveBeenCalledWith({ email: agent.email, password: 'correct-password' });
    expect(sessionStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)).toBe('test-jwt');
    expect(screen.getByText('Signed in as')).toHaveTextContent(agent.fullName);
  });

  it('restores a valid stored session and logout clears it and returns to login', async () => {
    const user = userEvent.setup();
    sessionStorage.setItem(ACCESS_TOKEN_STORAGE_KEY, 'restored-jwt');
    renderAuthApp('/tickets');

    expect(await screen.findByText('Ticket list page')).toBeInTheDocument();
    expect(mocks.getCurrentAgent).toHaveBeenCalledOnce();
    expect(screen.getByText('Signed in as')).toHaveTextContent(agent.fullName);

    await user.click(screen.getByRole('button', { name: 'Log out' }));
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    await waitFor(() => expect(sessionStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)).toBeNull());
  });

  it('sends a fresh login to the ticket list when no return destination exists', async () => {
    const user = userEvent.setup();
    mocks.login.mockResolvedValue({
      accessToken: 'test-jwt',
      expiresAtUtc: '2030-01-01T00:00:00Z',
      agent,
    });
    renderAuthApp('/login');

    await user.type(await screen.findByRole('textbox', { name: 'Email' }), agent.email);
    await user.type(screen.getByLabelText('Password'), 'correct-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Ticket list page')).toBeInTheDocument();
  });

  it('clears an invalid restored session and redirects to login', async () => {
    sessionStorage.setItem(ACCESS_TOKEN_STORAGE_KEY, 'invalid-jwt');
    mocks.getCurrentAgent.mockRejectedValue(new ApiError(401, 'Unauthorized'));
    renderAuthApp('/tickets/42?tab=history');

    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    await waitFor(() => expect(sessionStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)).toBeNull());
  });

  it('redirects after a protected API 401 and returns to the original route after reauthentication', async () => {
    const user = userEvent.setup();
    setAuthenticatedSession('expired-jwt', agent);
    mocks.login.mockResolvedValue({
      accessToken: 'renewed-test-jwt',
      expiresAtUtc: '2030-01-01T00:00:00Z',
      agent,
    });
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(
      new Response(JSON.stringify({ title: 'Unauthorized' }), { status: 401, statusText: 'Unauthorized' }),
    ));

    render(
      <MemoryRouter initialEntries={['/tickets/42?tab=history']}>
        <AuthProvider>
          <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route element={<RequireAuth />}>
              <Route path="/" element={<App />}>
                <Route path="tickets/:id" element={<><p>Ticket detail page</p><CurrentLocation /><TriggerProtectedRequest /></>} />
              </Route>
            </Route>
          </Routes>
        </AuthProvider>
      </MemoryRouter>,
    );

    expect(await screen.findByText('Ticket detail page')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Load protected data' }));
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
    expect(sessionStorage.getItem(ACCESS_TOKEN_STORAGE_KEY)).toBeNull();

    await user.type(screen.getByRole('textbox', { name: 'Email' }), agent.email);
    await user.type(screen.getByLabelText('Password'), 'correct-password');
    await user.click(screen.getByRole('button', { name: 'Sign in' }));

    expect(await screen.findByText('Ticket detail page')).toBeInTheDocument();
    expect(screen.getByTestId('current-location')).toHaveTextContent('/tickets/42?tab=history');
  });
});
