import { useTranslation } from 'react-i18next';
import type { CapacityChangeResult } from '../types/admin';

/**
 * Phase 20 WP1 3.4: tells an admin what a capacity change (block, deactivate) did to the
 * bookings that already existed. Renders nothing when nothing was affected.
 */
export function CapacityImpactBanner({ impact, onDismiss }: { impact: CapacityChangeResult | null; onDismiss: () => void }) {
  const { t } = useTranslation();
  if (!impact) return null;

  const tone = impact.affected === 0
    ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
    : 'border-amber-200 bg-amber-50 text-amber-800';

  return (
    <div role="status" className={`mt-5 flex items-start gap-2.5 rounded-lg border px-3.5 py-3 text-[13px] ${tone}`}>
      <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <span className="flex-1 num">
        {impact.affected === 0
          ? t('admin.capacityImpact.none')
          : t('admin.capacityImpact.result', {
              affected: impact.affected,
              reassigned: impact.reassigned,
              waitlisted: impact.waitlisted,
              cancelled: impact.cancelled,
            })}
      </span>
      <button
        type="button"
        onClick={onDismiss}
        aria-label={t('common.close')}
        className="-m-1 grid h-7 w-7 place-items-center rounded-md hover:bg-black/5"
      >
        <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
          <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
        </svg>
      </button>
    </div>
  );
}
