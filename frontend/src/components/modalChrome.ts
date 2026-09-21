import { useEffect } from 'react';

/** Overlay + panel classes shared by Modal and ConfirmDialog: centered card from `sm`, bottom sheet below. */
export const modalOverlayClass =
  'fixed inset-0 z-50 flex items-end justify-center bg-ink-900/55 p-0 backdrop-blur-[2px] sm:items-center sm:p-4';

export function modalPanelClass(size: 'sm' | 'md' = 'md'): string {
  return [
    'flex max-h-[90dvh] w-full flex-col overflow-hidden rounded-t-card bg-white shadow-pop ring-1 ring-line sm:rounded-card',
    size === 'sm' ? 'sm:max-w-sm' : 'sm:max-w-md',
  ].join(' ');
}

/** Button row inside a dialog footer: stacked on phones, right-aligned from `sm` up. */
export const modalActionsClass = 'flex flex-col-reverse gap-2 sm:flex-row sm:items-center sm:justify-end';

export function useEscapeToClose(onClose: () => void, enabled = true): void {
  useEffect(() => {
    if (!enabled) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose, enabled]);
}

