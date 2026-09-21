import { useState, type InputHTMLAttributes } from 'react';
import { useTranslation } from 'react-i18next';

type Props = Omit<InputHTMLAttributes<HTMLInputElement>, 'type' | 'className'> & {
  /** Classes for the <input>; right padding for the toggle button is added. */
  className?: string;
};

/**
 * Password field with a show/hide toggle. The toggle is a real button (not part
 * of the form submission) and sits inside the field's right edge; it is sized
 * 44px for touch.
 */
export function PasswordInput({ className = '', ...inputProps }: Props) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(false);

  return (
    <div className="relative">
      <input {...inputProps} type={visible ? 'text' : 'password'} className={`${className} pr-11`} />
      <button
        type="button"
        onClick={() => setVisible((v) => !v)}
        aria-label={visible ? t('common.hidePassword') : t('common.showPassword')}
        aria-pressed={visible}
        tabIndex={-1}
        className="absolute inset-y-0 right-0 grid w-11 place-items-center text-ink-400 transition-colors hover:text-ink-700"
      >
        {visible ? (
          <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={1.75}
              d="M3 3l18 18M10.6 10.6a3 3 0 004.2 4.2M9.9 5.1A10.4 10.4 0 0112 4.9c5 0 8.6 3.6 10 7.1a11.8 11.8 0 01-3.2 4.3M6.6 6.6C4.4 8 3 10 2 12c1.4 3.5 5 7.1 10 7.1 1.7 0 3.2-.4 4.6-1.1"
            />
          </svg>
        ) : (
          <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" aria-hidden="true">
            <path
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeWidth={1.75}
              d="M2 12c1.4-3.5 5-7.1 10-7.1s8.6 3.6 10 7.1c-1.4 3.5-5 7.1-10 7.1S3.4 15.5 2 12z"
            />
            <circle cx="12" cy="12" r="3" strokeWidth={1.75} />
          </svg>
        )}
      </button>
    </div>
  );
}
