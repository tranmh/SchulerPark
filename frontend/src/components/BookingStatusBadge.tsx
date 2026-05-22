import { useTranslation } from 'react-i18next';
import type { BookingStatus } from '../types/booking';

const statusStyles: Record<BookingStatus, { dot: string; pill: string }> = {
  Pending:   { dot: 'bg-amber-500',   pill: 'bg-amber-50 text-amber-800 ring-amber-200' },
  Won:       { dot: 'bg-emerald-500', pill: 'bg-emerald-50 text-emerald-800 ring-emerald-200' },
  Lost:      { dot: 'bg-rose-500',    pill: 'bg-rose-50 text-rose-700 ring-rose-200' },
  Confirmed: { dot: 'bg-brand-500',   pill: 'bg-brand-50 text-brand-800 ring-brand-200' },
  Cancelled: { dot: 'bg-ink-300',     pill: 'bg-ink-100 text-ink-500 ring-line' },
  Expired:   { dot: 'bg-ink-300',     pill: 'bg-ink-100 text-ink-500 ring-line' },
};

interface Props {
  status: BookingStatus;
}

export function BookingStatusBadge({ status }: Props) {
  const { t } = useTranslation();
  const style = statusStyles[status];
  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[11.5px] font-semibold ring-1 ring-inset ${style.pill}`}
    >
      <span className={`h-1.5 w-1.5 rounded-full ${style.dot}`} />
      {t(`components.status.${status}`)}
    </span>
  );
}
