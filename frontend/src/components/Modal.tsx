import type { ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { useBodyScrollLock } from '../hooks/useBodyScrollLock';
import { modalOverlayClass, modalPanelClass, useEscapeToClose } from './modalChrome';

/**
 * Shared dialog chrome. Centered card from `sm` up, bottom sheet on phones
 * (full width, rounded top corners, clears the home indicator).
 * Escape closes it; page scroll is locked while it is mounted.
 */

interface Props {
  title: ReactNode;
  onClose: () => void;
  children: ReactNode;
  /** Optional line under the title. */
  subtitle?: ReactNode;
  /** Optional action row; rendered in a bordered footer. */
  footer?: ReactNode;
  size?: 'sm' | 'md';
}

export function Modal({ title, subtitle, onClose, children, footer, size = 'md' }: Props) {
  const { t } = useTranslation();
  useEscapeToClose(onClose);
  useBodyScrollLock(true);

  return (
    <div className={modalOverlayClass} onClick={onClose}>
      <div
        role="dialog"
        aria-modal="true"
        className={modalPanelClass(size)}
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-start justify-between gap-3 border-b border-line px-5 py-4 sm:px-6">
          <div className="min-w-0">
            <h3 className="text-[15.5px] font-semibold text-ink-900">{title}</h3>
            {subtitle && <p className="mt-1 text-[12.5px] text-ink-400">{subtitle}</p>}
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label={t('common.close')}
            className="-mr-2 -mt-1.5 grid h-10 w-10 shrink-0 place-items-center rounded-md text-ink-400 hover:bg-line/60 hover:text-ink-900"
          >
            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8} d="M6 6l12 12M6 18L18 6" />
            </svg>
          </button>
        </div>
        <div className={`overflow-y-auto px-5 py-5 sm:px-6 ${footer ? '' : 'pb-[calc(1.25rem+env(safe-area-inset-bottom))] sm:pb-5'}`}>
          {children}
        </div>
        {footer && (
          <div className="border-t border-line bg-surface-sunken/60 px-5 py-3.5 pb-[calc(0.875rem+env(safe-area-inset-bottom))] sm:px-6 sm:pb-3.5">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
