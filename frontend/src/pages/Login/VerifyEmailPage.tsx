import { useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { authService } from '../../services/authService';
import { LanguageToggle } from '../../components/LanguageToggle';

type Status = 'verifying' | 'success' | 'failed';

export function VerifyEmailPage() {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();
  const [status, setStatus] = useState<Status>('verifying');
  const startedRef = useRef(false);

  const token = searchParams.get('token') ?? '';

  useEffect(() => {
    // Guard against React StrictMode double-invoke: the token is single-use,
    // so a second call would report a false failure.
    if (startedRef.current) return;
    startedRef.current = true;

    if (!token) {
      setStatus('failed');
      return;
    }

    authService
      .verifyEmail(token)
      .then(() => setStatus('success'))
      .catch(() => setStatus('failed'));
  }, [token]);

  const title =
    status === 'verifying' ? t('auth.verifyingEmail')
    : status === 'success' ? t('auth.emailVerifiedTitle')
    : t('auth.emailVerifyFailedTitle');

  const body =
    status === 'verifying' ? ''
    : status === 'success' ? t('auth.emailVerifiedBody')
    : t('auth.emailVerifyFailedBody');

  return (
    <div className="relative flex min-h-screen items-center justify-center bg-surface-sunken px-4 py-12">
      <div className="absolute right-6 top-6">
        <LanguageToggle variant="light" />
      </div>
      <div className="w-full max-w-sm rounded-card border border-line bg-white p-8 text-center shadow-card">
        <span className="mx-auto grid h-10 w-10 place-items-center rounded-xl bg-gradient-to-br from-brand-400 to-brand-700 text-[15px] font-extrabold text-white shadow-[inset_0_1px_0_rgba(255,255,255,0.15)]">
          SP
        </span>
        <h1 className="mt-4 text-[22px] font-bold tracking-tight text-ink-900">{title}</h1>
        {body && <p className="mt-3 text-[13.5px] leading-relaxed text-ink-500">{body}</p>}
        {status !== 'verifying' && (
          <Link
            to="/login"
            className="mt-6 inline-block w-full rounded-lg bg-brand-500 px-4 py-2.5 text-[14px] font-medium text-white shadow-sm transition-colors hover:bg-brand-600"
          >
            {t('auth.backToLogin')}
          </Link>
        )}
      </div>
    </div>
  );
}
