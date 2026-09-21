import { useTranslation } from 'react-i18next';
import { useOnlineStatus } from '../hooks/useOnlineStatus';

/**
 * Shown while the browser reports itself offline. The service worker serves the
 * app shell in that state (see sw.ts), so without this the user would see a
 * normal-looking page whose every API call quietly fails.
 */
export function OfflineBanner() {
  const { t } = useTranslation();
  const online = useOnlineStatus();

  if (online) return null;

  return (
    <div
      role="status"
      aria-live="polite"
      data-testid="offline-banner"
      className="fixed inset-x-0 top-0 z-50 flex items-center justify-center gap-2.5 bg-amber-400 px-4 py-2 text-[13px] font-medium text-amber-950 shadow-md"
    >
      <svg className="h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth={1.75}
          d="M3 3l18 18M8.5 16.5a5 5 0 017 0M5.7 13.4a9 9 0 013.2-2.1M12 20h.01M2.9 10.4a13 13 0 014.3-2.8M21.1 10.4a13 13 0 00-6.2-3.3M18.3 13.4a9 9 0 00-2-1.5"
        />
      </svg>
      <span>{t('offline.banner')}</span>
    </div>
  );
}
