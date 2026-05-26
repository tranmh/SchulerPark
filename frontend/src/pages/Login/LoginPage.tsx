import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../contexts/AuthContext';
import { LanguageToggle } from '../../components/LanguageToggle';

export function LoginPage() {
  const { t } = useTranslation();
  const { login, loginWithAzureAd, authConfig, isAuthenticated } = useAuth();
  const navigate = useNavigate();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  if (isAuthenticated) {
    navigate('/', { replace: true });
    return null;
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    try {
      await login(email, password);
      navigate('/');
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error
        || t('auth.loginFailed');
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  const handleAzureAd = async () => {
    setError('');
    setLoading(true);

    try {
      await loginWithAzureAd();
      navigate('/');
    } catch {
      setError(t('auth.azureFailed'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="grid min-h-screen lg:grid-cols-2">
      {/* Brand panel */}
      <div className="relative hidden overflow-hidden bg-gradient-to-br from-brand-700 to-ink-900 lg:block">
        <div
          className="pointer-events-none absolute -right-32 -top-32 h-[28rem] w-[28rem] rounded-full"
          style={{ background: 'radial-gradient(circle, rgba(107,176,189,0.18), transparent 70%)' }}
        />
        <div
          className="pointer-events-none absolute -bottom-40 -left-32 h-[28rem] w-[28rem] rounded-full"
          style={{ background: 'radial-gradient(circle, rgba(63,140,157,0.14), transparent 70%)' }}
        />
        <div className="relative z-10 flex h-full flex-col p-12 text-white">
          <div className="flex items-center gap-2.5">
            <span className="grid h-9 w-9 place-items-center rounded-lg bg-gradient-to-br from-brand-300 to-brand-700 text-[14px] font-extrabold tracking-tight text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15)]">
              LE
            </span>
            <span className="text-[15px] font-semibold tracking-tight">LouisE</span>
          </div>

          <div className="mt-auto">
            <div className="text-[32px] font-bold leading-[1.15] tracking-tight">{t('auth.tagline')}</div>
            <p className="mt-4 max-w-md text-[14px] leading-relaxed text-brand-200">
              {t('auth.taglineSubtitle')}
            </p>
            <div className="mt-10 flex items-center gap-2 text-[12px] text-brand-300">
              <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
              </svg>
              {t('auth.ssoNote')}
            </div>
          </div>
        </div>
      </div>

      {/* Form */}
      <div className="relative flex items-center justify-center bg-white px-6 py-12 lg:px-12">
        <div className="absolute right-6 top-6">
          <LanguageToggle variant="light" />
        </div>
        <div className="w-full max-w-sm">
          <div className="lg:hidden mb-8 flex items-center gap-2.5">
            <span className="grid h-9 w-9 place-items-center rounded-lg bg-gradient-to-br from-brand-400 to-brand-700 text-[14px] font-extrabold text-white">
              LE
            </span>
            <span className="text-[15px] font-semibold tracking-tight text-ink-900">LouisE</span>
          </div>
          <h1 className="text-[24px] font-bold tracking-tight text-ink-900">{t('auth.welcomeBack')}</h1>
          <p className="mt-1 text-[13.5px] text-ink-400">{t('auth.welcomeBackSubtitle')}</p>

          {error && (
            <div className="mt-5 flex items-start gap-2.5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
              <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
              </svg>
              <span>{error}</span>
            </div>
          )}

          <form onSubmit={handleSubmit} className="mt-7 space-y-4">
            <div>
              <label htmlFor="email" className="mb-1.5 block text-[12.5px] font-medium text-ink-500">
                {t('auth.email')}
              </label>
              <input
                id="email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                required
                autoComplete="email"
                className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900 placeholder:text-ink-300 transition-shadow"
              />
            </div>

            <div>
              <div className="mb-1.5 flex items-center justify-between">
                <label htmlFor="password" className="text-[12.5px] font-medium text-ink-500">
                  {t('auth.password')}
                </label>
                <a className="text-[12px] font-medium text-brand-500 hover:text-brand-700" href="#">
                  {t('auth.forgot')}
                </a>
              </div>
              <input
                id="password"
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                autoComplete="current-password"
                className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900 placeholder:text-ink-300 transition-shadow"
              />
            </div>

            <button
              type="submit"
              disabled={loading}
              className="mt-2 w-full rounded-lg bg-brand-500 px-4 py-2.5 text-[14px] font-medium text-white shadow-sm transition-colors hover:bg-brand-600 disabled:opacity-60"
            >
              {loading ? t('auth.signingIn') : t('auth.signIn')}
            </button>
          </form>

          {authConfig?.azureAdEnabled && (
            <>
              <div className="relative my-5">
                <div className="absolute inset-0 flex items-center">
                  <div className="w-full border-t border-line" />
                </div>
                <div className="relative flex justify-center">
                  <span className="bg-white px-3 text-[11px] font-medium uppercase tracking-wider text-ink-300">
                    {t('auth.or')}
                  </span>
                </div>
              </div>

              <button
                type="button"
                onClick={handleAzureAd}
                disabled={loading}
                className="flex w-full items-center justify-center gap-2.5 rounded-lg border border-line-strong bg-white px-4 py-2.5 text-[14px] font-medium text-ink-700 transition-colors hover:bg-surface-sunken disabled:opacity-60"
              >
                <svg className="h-4 w-4" viewBox="0 0 23 23">
                  <path fill="#F25022" d="M1 1h10v10H1z" />
                  <path fill="#7FBA00" d="M12 1h10v10H12z" />
                  <path fill="#00A4EF" d="M1 12h10v10H1z" />
                  <path fill="#FFB900" d="M12 12h10v10H12z" />
                </svg>
                {t('auth.continueMicrosoft')}
              </button>
            </>
          )}

          <p className="mt-7 text-center text-[13px] text-ink-400">
            {t('auth.newHere')}{' '}
            <Link to="/register" className="font-medium text-brand-500 hover:text-brand-700">
              {t('auth.createAccount')}
            </Link>
          </p>
        </div>
      </div>
    </div>
  );
}
