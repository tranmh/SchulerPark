import { useEffect, useState, useMemo } from 'react';
import { adminService } from '../../services/adminService';
import { CalendarPicker } from '../../components/CalendarPicker';
import type { AdminLocation, AdminBlockedDay } from '../../types/admin';

export function BlockedDaysPage() {
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [selectedLocationId, setSelectedLocationId] = useState<string | null>(null);
  const [blockedDays, setBlockedDays] = useState<AdminBlockedDay[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Add block modal
  const [selectedDate, setSelectedDate] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    adminService.getLocations()
      .then((locs) => { setLocations(locs); if (locs.length > 0) setSelectedLocationId(locs[0].id); })
      .catch(() => setError('Failed to load locations.'))
      .finally(() => setLoading(false));
  }, []);

  const loadBlockedDays = async () => {
    if (!selectedLocationId) return;
    try {
      setBlockedDays(await adminService.getBlockedDays(selectedLocationId));
    } catch { setError('Failed to load blocked days.'); }
  };

  useEffect(() => { loadBlockedDays(); }, [selectedLocationId]);

  const blockedDates = useMemo(() => {
    const set = new Set<string>();
    for (const b of blockedDays) {
      if (!b.parkingSlotId) set.add(b.date);
    }
    return set;
  }, [blockedDays]);

  const handleDateClick = async (date: string) => {
    if (!selectedLocationId) return;
    setError(null);

    // If already blocked (location-wide), unblock it
    const existing = blockedDays.find((b) => b.date === date && !b.parkingSlotId);
    if (existing) {
      try {
        await adminService.removeBlockedDay(existing.id);
        await loadBlockedDays();
      } catch { setError('Failed to remove block.'); }
      return;
    }

    // Otherwise, show reason input and add block
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
    } catch { setError('Failed to block day.'); }
    finally { setSaving(false); }
  };

  if (loading) return <div className="flex h-64 items-center justify-center text-gray-400">Loading...</div>;

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Blocked Days</h1>
      <p className="mt-1 text-sm text-gray-500">Click a date to block/unblock it. Red dates are blocked.</p>

      {error && <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}

      <div className="mt-4">
        <label className="block text-sm font-medium text-gray-700">Location</label>
        <select value={selectedLocationId ?? ''} onChange={(e) => setSelectedLocationId(e.target.value)}
          className="mt-1 rounded-md border border-gray-300 px-3 py-2 text-sm">
          {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
        </select>
      </div>

      <div className="mt-6 flex gap-8">
        <CalendarPicker
          selectedDate={null}
          onSelect={handleDateClick}
          blockedDates={blockedDates}
        />

        <div className="flex-1">
          <h3 className="text-sm font-semibold text-gray-700">Blocked Days List</h3>
          <div className="mt-2 max-h-96 space-y-2 overflow-auto">
            {blockedDays.filter((b) => !b.parkingSlotId).length === 0 ? (
              <p className="text-sm text-gray-400">No blocked days for this location.</p>
            ) : (
              blockedDays.filter((b) => !b.parkingSlotId).map((b) => (
                <div key={b.id} className="flex items-center justify-between rounded-md border border-gray-200 bg-white px-3 py-2">
                  <div>
                    <span className="text-sm font-medium text-gray-900">{b.date}</span>
                    {b.reason && <span className="ml-2 text-sm text-gray-500">— {b.reason}</span>}
                  </div>
                  <button onClick={async () => { await adminService.removeBlockedDay(b.id); await loadBlockedDays(); }}
                    className="text-sm text-red-600 hover:text-red-800">Remove</button>
                </div>
              ))
            )}
          </div>
        </div>
      </div>

      {/* Add block modal */}
      {selectedDate && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="mx-4 w-full max-w-sm rounded-lg bg-white p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900">Block {selectedDate}</h3>
            <div className="mt-4">
              <label className="block text-sm font-medium text-gray-700">Reason (optional)</label>
              <input value={reason} onChange={(e) => setReason(e.target.value)}
                className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm" placeholder="e.g. Public holiday" />
            </div>
            <div className="mt-4 flex justify-end gap-3">
              <button onClick={() => { setSelectedDate(null); setReason(''); }}
                className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50">Cancel</button>
              <button onClick={handleAddBlock} disabled={saving}
                className="rounded-md bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700 disabled:opacity-50">
                {saving ? 'Blocking...' : 'Block Day'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
