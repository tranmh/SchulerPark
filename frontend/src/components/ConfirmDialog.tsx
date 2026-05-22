import { useTranslation } from 'react-i18next';

interface Props {
  isOpen: boolean;
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  onConfirm: () => void;
  onCancel: () => void;
  isLoading?: boolean;
  /** Visual tone of the confirm button. Defaults to danger. */
  tone?: 'danger' | 'primary';
}

export function ConfirmDialog({
  isOpen,
  title,
  message,
  confirmLabel,
  cancelLabel,
  onConfirm,
  onCancel,
  isLoading = false,
  tone = 'danger',
}: Props) {
  const { t } = useTranslation();
  if (!isOpen) return null;

  const confirmClass =
    tone === 'danger'
      ? 'bg-rose-600 hover:bg-rose-700 text-white'
      : 'bg-brand-500 hover:bg-brand-600 text-white';

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink-900/55 backdrop-blur-[2px] p-4">
      <div className="w-full max-w-md rounded-card bg-white shadow-pop ring-1 ring-line">
        <div className="p-6">
          <div className="flex items-start gap-4">
            {tone === 'danger' ? (
              <div className="grid h-10 w-10 shrink-0 place-items-center rounded-lg bg-rose-50 text-rose-600">
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path
                    strokeLinecap="round"
                    strokeLinejoin="round"
                    strokeWidth={1.5}
                    d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"
                  />
                </svg>
              </div>
            ) : (
              <div className="grid h-10 w-10 shrink-0 place-items-center rounded-lg bg-brand-50 text-brand-600">
                <svg className="h-5 w-5" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
              </div>
            )}
            <div className="flex-1">
              <h3 className="text-[15.5px] font-semibold text-ink-900">{title}</h3>
              <p className="mt-1.5 text-[13.5px] leading-relaxed text-ink-500">{message}</p>
            </div>
          </div>
        </div>
        <div className="flex items-center justify-end gap-2 rounded-b-card border-t border-line bg-surface-sunken/60 px-6 py-3.5">
          <button
            type="button"
            onClick={onCancel}
            disabled={isLoading}
            className="rounded-lg border border-line-strong bg-white px-4 py-2 text-[13px] font-medium text-ink-700 hover:bg-surface-sunken disabled:opacity-50"
          >
            {cancelLabel ?? t('common.cancel')}
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={isLoading}
            className={`rounded-lg px-4 py-2 text-[13px] font-medium shadow-sm disabled:opacity-50 ${confirmClass}`}
          >
            {isLoading ? t('common.processing') : (confirmLabel ?? t('common.confirm'))}
          </button>
        </div>
      </div>
    </div>
  );
}
