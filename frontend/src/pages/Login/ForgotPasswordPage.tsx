import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { authService } from '../../services/authService';
import { LanguageToggle } from '../../components/LanguageToggle';

/**
 * Phase 20 WP2: request a password-reset link. The endpoint is deliberately
 * non-committal (202 whether or not the address has an account), so the page
 * shows the same "check your email" card either way — no account enumeration.
 */
export function ForgotPasswordPage() {
  const { t } = useTranslation();
  const [email, setEmail] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await authService.forgotPassword(email);
      setSubmitted(true);
    } catch {
      setError(t('auth.forgotFailed'));
    } finally {
      setLoading(false);
    }
  };

  if (submitted) {
    return (
      <div className="relative flex min-h-dvh items-center justify-center bg-surface-sunken px-4 pb-12 pt-[calc(4.5rem+env(safe-area-inset-top))] sm:py-12">
        <div className="absolute right-4 top-[calc(1rem+env(safe-area-inset-top))] sm:right-6 sm:top-6">
          <LanguageToggle variant="light" />
        </div>
        <div className="w-full max-w-sm rounded-card border border-line bg-white p-8 text-center shadow-card">
          <span className="mx-auto grid h-10 w-10 place-items-center rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 text-[15px] font-extrabold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15)]">
            SP
          </span>
          <h1 className="mt-4 text-[22px] font-bold tracking-tight text-ink-900">{t('auth.forgotSentTitle')}</h1>
          <p className="mt-3 text-[13.5px] leading-relaxed text-ink-500">
            {t('auth.forgotSentBody', { email })}
          </p>
          <Link
            to="/login"
            className="mt-6 inline-block w-full rounded-lg bg-brand-500 px-4 py-2.5 text-[14px] font-medium text-white shadow-sm transition-colors hover:bg-brand-600"
          >
            {t('auth.backToLogin')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="relative flex min-h-dvh items-center justify-center bg-surface-sunken px-4 pb-12 pt-[calc(4.5rem+env(safe-area-inset-top))] sm:py-12">
      <div className="absolute right-4 top-[calc(1rem+env(safe-area-inset-top))] sm:right-6 sm:top-6">
        <LanguageToggle variant="light" />
      </div>
      <div className="w-full max-w-sm rounded-card border border-line bg-white p-8 shadow-card">
        <div className="text-center">
          <span className="mx-auto grid h-10 w-10 place-items-center rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 text-[15px] font-extrabold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15)]">
            SP
          </span>
          <h1 className="mt-4 text-[22px] font-bold tracking-tight text-ink-900">{t('auth.forgotTitle')}</h1>
          <p className="mt-1 text-[13px] text-ink-400">{t('auth.forgotBody')}</p>
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
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-base text-ink-900 sm:text-[14px]"
            />
          </div>

          <button
            type="submit"
            disabled={loading}
            className="mt-2 min-h-11 w-full rounded-lg bg-brand-500 px-4 py-2.5 text-[14px] font-medium text-white shadow-sm transition-colors hover:bg-brand-600 disabled:opacity-60"
          >
            {loading ? t('auth.forgotSending') : t('auth.forgotSubmit')}
          </button>
        </form>

        <p className="mt-6 text-center text-[13px] text-ink-400">
          <Link to="/login" className="font-medium text-brand-500 hover:text-brand-700">{t('auth.backToLogin')}</Link>
        </p>
      </div>
    </div>
  );
}
