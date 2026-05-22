import { useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../contexts/AuthContext';
import { LanguageToggle } from '../../components/LanguageToggle';

export function RegisterPage() {
  const { t } = useTranslation();
  const { register, isAuthenticated } = useAuth();
  const navigate = useNavigate();

  const [email, setEmail] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  if (isAuthenticated) {
    navigate('/', { replace: true });
    return null;
  }

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');

    if (password.length < 8) {
      setError(t('auth.passwordTooShort'));
      return;
    }

    if (password !== confirmPassword) {
      setError(t('auth.passwordsDoNotMatch'));
      return;
    }

    setLoading(true);

    try {
      await register(email, displayName, password);
      navigate('/');
    } catch (err: unknown) {
      const message =
        (err as { response?: { data?: { error?: string } } })?.response?.data?.error
        || t('auth.registerFailed');
      setError(message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="relative flex min-h-screen items-center justify-center bg-surface-sunken px-4 py-12">
      <div className="absolute right-6 top-6">
        <LanguageToggle variant="light" />
      </div>
      <div className="w-full max-w-sm rounded-card border border-line bg-white p-8 shadow-card">
        <div className="text-center">
          <span className="mx-auto grid h-10 w-10 place-items-center rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 text-[15px] font-extrabold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15)]">
            SP
          </span>
          <h1 className="mt-4 text-[22px] font-bold tracking-tight text-ink-900">{t('auth.registerTitle')}</h1>
          <p className="mt-1 text-[13px] text-ink-400">{t('auth.registerSubtitle')}</p>
        </div>

        {error && (
          <div className="mt-5 flex items-start gap-2.5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
            <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
            </svg>
            <span>{error}</span>
          </div>
        )}

        <form onSubmit={handleSubmit} className="mt-6 space-y-4">
          <div>
            <label htmlFor="email" className="mb-1.5 block text-[12.5px] font-medium text-ink-500">{t('auth.email')}</label>
            <input
              id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email"
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
            />
          </div>
          <div>
            <label htmlFor="displayName" className="mb-1.5 block text-[12.5px] font-medium text-ink-500">{t('auth.displayName')}</label>
            <input
              id="displayName" type="text" value={displayName} onChange={(e) => setDisplayName(e.target.value)} required autoComplete="name"
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
            />
          </div>
          <div>
            <label htmlFor="password" className="mb-1.5 block text-[12.5px] font-medium text-ink-500">{t('auth.password')}</label>
            <input
              id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required minLength={8} autoComplete="new-password"
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
            />
            <p className="mt-1.5 text-[11.5px] text-ink-400">{t('auth.passwordHint')}</p>
          </div>
          <div>
            <label htmlFor="confirmPassword" className="mb-1.5 block text-[12.5px] font-medium text-ink-500">{t('auth.confirmPassword')}</label>
            <input
              id="confirmPassword" type="password" value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} required autoComplete="new-password"
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
            />
          </div>

          <button
            type="submit"
            disabled={loading}
            className="mt-2 w-full rounded-lg bg-brand-500 px-4 py-2.5 text-[14px] font-medium text-white shadow-sm transition-colors hover:bg-brand-600 disabled:opacity-60"
          >
            {loading ? t('auth.creatingAccount') : t('auth.createAccountBtn')}
          </button>
        </form>

        <p className="mt-6 text-center text-[13px] text-ink-400">
          {t('auth.alreadyHaveAccount')}{' '}
          <Link to="/login" className="font-medium text-brand-500 hover:text-brand-700">{t('auth.signIn')}</Link>
        </p>
      </div>
    </div>
  );
}
