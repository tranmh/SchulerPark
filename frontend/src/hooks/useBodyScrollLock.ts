import { useEffect } from 'react';

/**
 * Locks page scrolling while `locked` is true (drawer, modal, bottom sheet).
 * Restores the previous `overflow` value on unlock/unmount so nested locks
 * don't clobber each other.
 */
export function useBodyScrollLock(locked: boolean): void {
  useEffect(() => {
    if (!locked) return;
    const previous = document.body.style.overflow;
    document.body.style.overflow = 'hidden';
    return () => {
      document.body.style.overflow = previous;
    };
  }, [locked]);
}
