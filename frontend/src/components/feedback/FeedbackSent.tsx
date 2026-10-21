import { useTranslation } from 'react-i18next';

/** Thank-you state shown in place of the form once the admins have been mailed. */
export function FeedbackSent({ onClose }: { onClose: () => void }) {
  const { t } = useTranslation();
  return (
    <div className="flex flex-col items-center gap-3 py-2 text-center">
      <div className="grid h-11 w-11 place-items-center rounded-full bg-emerald-50 text-emerald-600">
        <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
        </svg>
      </div>
      <p className="text-[13.5px] text-ink-700" role="status">{t('feedback.sent')}</p>
      <button
        type="button"
        onClick={onClose}
        className="min-h-11 rounded-lg border border-line-strong bg-white px-4 py-2 text-[13px] font-medium text-ink-700 hover:bg-surface-sunken sm:min-h-0"
      >
        {t('common.close')}
      </button>
    </div>
  );
}
