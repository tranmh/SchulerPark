import { useTranslation } from 'react-i18next';
import { usePwaUpdate } from '../hooks/usePwaUpdate';

/**
 * Bottom toast shown once a new release of the app has been installed by the
 * service worker and is waiting to take over. The user decides when to reload,
 * so a deploy never yanks the page away in the middle of a booking.
 */
export function UpdateToast() {
  const { t } = useTranslation();
  const { needRefresh, reload, dismiss } = usePwaUpdate();

  if (!needRefresh) return null;

  return (
    <div
      role="status"
      aria-live="polite"
      data-testid="update-toast"
      className="fixed inset-x-4 bottom-4 z-50 mx-auto flex max-w-md flex-wrap items-center justify-between gap-x-4 gap-y-2 rounded-lg bg-gray-900 px-4 py-3 text-sm text-white shadow-lg ring-1 ring-white/10"
    >
      <span className="flex-1 basis-48">{t('update.available')}</span>
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={dismiss}
          className="rounded-md px-2 py-1.5 text-gray-300 hover:bg-white/10 hover:text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
        >
          {t('update.dismiss')}
        </button>
        <button
          type="button"
          onClick={reload}
          className="rounded-md bg-blue-600 px-3 py-1.5 font-medium text-white hover:bg-blue-500 focus:outline-none focus-visible:ring-2 focus-visible:ring-white"
        >
          {t('update.reload')}
        </button>
      </div>
    </div>
  );
}
