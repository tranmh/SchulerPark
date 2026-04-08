import type { BookingStatus } from '../types/booking';

const statusConfig: Record<BookingStatus, { label: string; classes: string }> = {
  Pending: { label: 'Pending', classes: 'bg-amber-100 text-amber-800' },
  Won: { label: 'Won — Confirm!', classes: 'bg-green-100 text-green-800' },
  Lost: { label: 'Lost', classes: 'bg-red-100 text-red-800' },
  Confirmed: { label: 'Confirmed', classes: 'bg-blue-100 text-blue-800' },
  Cancelled: { label: 'Cancelled', classes: 'bg-gray-100 text-gray-500' },
  Expired: { label: 'Expired', classes: 'bg-gray-100 text-gray-500' },
};

interface Props {
  status: BookingStatus;
}

export function BookingStatusBadge({ status }: Props) {
  const config = statusConfig[status];
  return (
    <span className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-medium ${config.classes}`}>
      {config.label}
    </span>
  );
}
