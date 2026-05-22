import { useTranslation } from 'react-i18next';

interface Props {
  variant?: 'light' | 'dark';
  className?: string;
}

const OPTIONS = [
  { code: 'de', label: 'DE' },
  { code: 'en', label: 'EN' },
] as const;

export function LanguageToggle({ variant = 'light', className = '' }: Props) {
  const { i18n, t } = useTranslation();
  const current = i18n.language.startsWith('en') ? 'en' : 'de';

  const containerCls =
    variant === 'dark'
      ? 'inline-flex items-center gap-0.5 rounded-md bg-white/[0.04] p-0.5'
      : 'inline-flex items-center gap-0.5 rounded-md border border-line bg-white p-0.5 shadow-sm';

  const activeCls =
    variant === 'dark'
      ? 'bg-white/15 text-white'
      : 'bg-brand-500 text-white shadow-sm';

  const inactiveCls =
    variant === 'dark'
      ? 'text-ink-300 hover:text-white'
      : 'text-ink-500 hover:text-ink-900';

  return (
    <div className={`${containerCls} ${className}`} role="group" aria-label={t('common.language')}>
      {OPTIONS.map(({ code, label }) => (
        <button
          key={code}
          type="button"
          onClick={() => {
            if (current !== code) void i18n.changeLanguage(code);
          }}
          aria-pressed={current === code}
          className={`rounded px-2 py-0.5 text-[11px] font-semibold tracking-wider transition-colors ${
            current === code ? activeCls : inactiveCls
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
