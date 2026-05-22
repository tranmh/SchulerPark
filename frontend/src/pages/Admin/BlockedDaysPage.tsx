import { useEffect, useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { adminService } from '../../services/adminService';
import { CalendarPicker } from '../../components/CalendarPicker';
import { LoadingSpinner } from '../../components/LoadingSpinner';
import type { AdminLocation, AdminBlockedDay } from '../../types/admin';

export function BlockedDaysPage() {
  const { t } = useTranslation();
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [selectedLocationId, setSelectedLocationId] = useState<string | null>(null);
  const [blockedDays, setBlockedDays] = useState<AdminBlockedDay[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    adminService.getLocations()
      .then((locs) => {
        setLocations(locs);
        if (locs.length > 0) setSelectedLocationId(locs[0].id);
      })
      .catch(() => setError('Failed to load locations.'))
      .finally(() => setLoading(false));
  }, []);

  const loadBlockedDays = async () => {
    if (!selectedLocationId) return;
    try {
      setBlockedDays(await adminService.getBlockedDays(selectedLocationId));
    } catch {
      setError('Failed to load blocked days.');
    }
  };

  useEffect(() => {
    loadBlockedDays();
  }, [selectedLocationId]);

  const blockedDates = useMemo(() => {
    const set = new Set<string>();
    for (const b of blockedDays) {
      if (!b.parkingSlotId) set.add(b.date);
    }
    return set;
  }, [blockedDays]);

  const locationWideBlocks = blockedDays.filter((b) => !b.parkingSlotId);

  const handleDateClick = async (date: string) => {
    if (!selectedLocationId) return;
    setError(null);

    const existing = blockedDays.find((b) => b.date === date && !b.parkingSlotId);
    if (existing) {
      try {
        await adminService.removeBlockedDay(existing.id);
        await loadBlockedDays();
      } catch {
        setError('Failed to remove block.');
      }
      return;
    }
    setSelectedDate(date);
  };

  const handleAddBlock = async () => {
    if (!selectedLocationId || !selectedDate) return;
    setSaving(true);
    setError(null);
    try {
      await adminService.createBlockedDay({
        locationId: selectedLocationId,
        date: selectedDate,
        reason: reason || undefined,
      });
      setSelectedDate(null);
      setReason('');
      await loadBlockedDays();
    } catch {
      setError('Failed to block day.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) return <LoadingSpinner />;

  return (
    <div>
      <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('admin.blockedDays')}</h1>
      <p className="mt-1 text-[13.5px] text-ink-400">
        Click a date on the calendar to block or unblock it. Bookings can't be made on red dates.
      </p>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
          {error}
        </div>
      )}

      <div className="mt-6">
        <label className="mb-1.5 block text-[12.5px] font-medium text-ink-500">Location</label>
        <select
          id="blocked-days-location"
          value={selectedLocationId ?? ''}
          onChange={(e) => setSelectedLocationId(e.target.value)}
          className="w-72 rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
        >
          {locations.map((l) => (
            <option key={l.id} value={l.id}>{l.name}</option>
          ))}
        </select>
      </div>

      <div className="mt-6 grid gap-7 lg:grid-cols-[420px_1fr]">
        <CalendarPicker selectedDate={null} onSelect={handleDateClick} blockedDates={blockedDates} />

        <div className="rounded-card border border-line bg-white shadow-card">
          <div className="flex items-center justify-between border-b border-line px-5 py-4">
            <h3 className="text-[14px] font-semibold text-ink-900">Blocked days list</h3>
            <span className="rounded-full bg-rose-50 px-2.5 py-0.5 text-[11.5px] font-semibold text-rose-700 ring-1 ring-inset ring-rose-200 num">
              {locationWideBlocks.length}
            </span>
          </div>
          <div className="max-h-[28rem] overflow-auto scroll-thin p-5">
            {locationWideBlocks.length === 0 ? (
              <p className="text-[12.5px] text-ink-400">No blocked days for this location.</p>
            ) : (
              <ul className="space-y-2">
                {locationWideBlocks.map((b) => (
                  <li
                    key={b.id}
                    data-testid="blocked-day-row"
                    data-block-id={b.id}
                    className="flex items-center justify-between rounded-lg border border-line bg-white px-3.5 py-2.5"
                  >
                    <div>
                      <div className="flex items-center gap-2 text-[13px] font-medium text-ink-900 num">
                        <span className="h-1.5 w-1.5 rounded-full bg-rose-500" />
                        {b.date}
                      </div>
                      {b.reason && <div className="ml-3.5 mt-0.5 text-[12px] text-ink-400">{b.reason}</div>}
                    </div>
                    <button
                      type="button"
                      onClick={async () => {
                        await adminService.removeBlockedDay(b.id);
                        await loadBlockedDays();
                      }}
                      className="text-[12px] font-medium text-rose-600 hover:text-rose-700"
                    >
                      Remove
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        </div>
      </div>

      {selectedDate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink-900/55 backdrop-blur-[2px] p-4">
          <div className="w-full max-w-sm rounded-card bg-white shadow-pop ring-1 ring-line">
            <div className="border-b border-line px-6 py-4">
              <h3 className="text-[15.5px] font-semibold text-ink-900">
                Block <span className="num">{selectedDate}</span>
              </h3>
              <p className="mt-1 text-[12.5px] text-ink-400">No bookings will be possible on this day.</p>
            </div>
            <div className="px-6 py-5">
              <label className="mb-1.5 block text-[12.5px] font-medium text-ink-500">Reason (optional)</label>
              <input
                id="block-reason"
                value={reason}
                onChange={(e) => setReason(e.target.value)}
                placeholder="e.g. Public holiday"
                className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
              />
            </div>
            <div className="flex justify-end gap-2 border-t border-line px-6 py-3.5">
              <button
                type="button"
                onClick={() => {
                  setSelectedDate(null);
                  setReason('');
                }}
                className="rounded-lg border border-line-strong bg-white px-4 py-2 text-[13px] font-medium text-ink-700 hover:bg-surface-sunken"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleAddBlock}
                disabled={saving}
                className="rounded-lg bg-rose-600 px-4 py-2 text-[13px] font-medium text-white shadow-sm hover:bg-rose-700 disabled:opacity-60"
              >
                {saving ? 'Blocking…' : 'Block day'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
