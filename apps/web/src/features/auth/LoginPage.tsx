import { useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { ApiError, toErrorMessage } from '../../api/client';
import { useAuth } from '../../auth/useAuth';
import { Field } from '../../components/Field';
import { getPostLoginDestination } from './loginNavigation';

export function LoginPage() {
  const { status, signIn } = useAuth();
  const location = useLocation();
  const navigate = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [submitError, setSubmitError] = useState<string>();
  const [isSubmitting, setIsSubmitting] = useState(false);
  const submitting = useRef(false);
  const destination = getPostLoginDestination(location.state);

  if (status === 'loading') {
    return <p className="spinner" role="status">Checking your session…</p>;
  }

  if (status === 'authenticated') {
    return <Navigate to={destination} replace />;
  }

  const submit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submitting.current) {
      return;
    }

    const normalizedEmail = email.trim();
    const nextErrors: Record<string, string> = {};
    if (!normalizedEmail) {
      nextErrors.email = 'Enter your email address.';
    } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(normalizedEmail)) {
      nextErrors.email = 'Enter a valid email address.';
    }
    if (!password) {
      nextErrors.password = 'Enter your password.';
    }

    setFieldErrors(nextErrors);
    setSubmitError(undefined);
    if (Object.keys(nextErrors).length > 0) {
      return;
    }

    submitting.current = true;
    setIsSubmitting(true);
    try {
      await signIn({ email: normalizedEmail, password });
      navigate(destination, { replace: true });
    } catch (error) {
      const fallback =
        error instanceof ApiError && error.status === 401
          ? 'Email or password is incorrect.'
          : 'Could not sign in. Check your connection and try again.';
      setSubmitError(toErrorMessage(error, fallback));
    } finally {
      submitting.current = false;
      setIsSubmitting(false);
    }
  };

  return (
    <main className="app__main login-page">
      <section className="card login-card" aria-labelledby="login-title">
        <div className="page-header">
          <div>
            <h1 id="login-title">Sign in</h1>
            <p className="page-header__subtitle">Use your support agent account.</p>
          </div>
        </div>
        <form className="form" onSubmit={submit} noValidate>
          <Field id="login-email" label="Email" error={fieldErrors.email}>
            {(props) => (
              <input
                {...props}
                type="email"
                required
                autoComplete="username"
                value={email}
                disabled={isSubmitting}
                onChange={(event) => setEmail(event.target.value)}
              />
            )}
          </Field>
          <Field id="login-password" label="Password" error={fieldErrors.password}>
            {(props) => (
              <input
                {...props}
                type="password"
                required
                autoComplete="current-password"
                value={password}
                disabled={isSubmitting}
                onChange={(event) => setPassword(event.target.value)}
              />
            )}
          </Field>
          {submitError && <p className="field__error" role="alert">{submitError}</p>}
          <button className="button button--primary" type="submit" disabled={isSubmitting}>
            {isSubmitting ? 'Signing in…' : 'Sign in'}
          </button>
        </form>
      </section>
    </main>
  );
}
