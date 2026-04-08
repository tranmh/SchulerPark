import { useEffect, useState, useCallback } from 'react';
import { adminService } from '../../services/adminService';
import type { AdminLocation, LotteryRun } from '../../types/admin';

export function LotteryHistoryPage() {
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [runs, setRuns] = useState<LotteryRun[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Filters
  const [locationFilter, setLocationFilter] = useState('');

  const pageSize = 20;

  useEffect(() => {
    adminService.getLocations().then(setLocations).catch(() => {});
  }, []);

  const loadRuns = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await adminService.getLotteryRuns({
        locationId: locationFilter || undefined,
        page,
        pageSize,
      });
      setRuns(res.lotteryRuns);
      setTotalCount(res.totalCount);
    } catch { setError('Failed to load lottery runs.'); }
    finally { setLoading(false); }
  }, [locationFilter, page]);

  useEffect(() => { loadRuns(); }, [loadRuns]);

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Lottery History</h1>
      <p className="mt-1 text-sm text-gray-500">{totalCount} run{totalCount !== 1 ? 's' : ''} total</p>

      {error && <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}

      <div className="mt-4">
        <select value={locationFilter} onChange={(e) => { setLocationFilter(e.target.value); setPage(1); }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm">
          <option value="">All Locations</option>
          {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
        </select>
      </div>

      <div className="mt-6 overflow-hidden rounded-lg border border-gray-200">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Ran At</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Location</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Date</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Time Slot</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Algorithm</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Bookings</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Avail. Slots</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 bg-white">
            {loading ? (
              <tr><td colSpan={7} className="px-4 py-8 text-center text-sm text-gray-400">Loading...</td></tr>
            ) : runs.length === 0 ? (
              <tr><td colSpan={7} className="px-4 py-8 text-center text-sm text-gray-400">No lottery runs found.</td></tr>
            ) : runs.map((r) => (
              <tr key={r.id}>
                <td className="px-4 py-3 text-sm text-gray-900">
                  {new Date(r.ranAt).toLocaleString('de-DE', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' })}
                </td>
                <td className="px-4 py-3 text-sm text-gray-500">{r.locationName}</td>
                <td className="px-4 py-3 text-sm text-gray-500">{r.date}</td>
                <td className="px-4 py-3 text-sm text-gray-500">{r.timeSlot}</td>
                <td className="px-4 py-3">
                  <span className="inline-flex rounded-full bg-purple-100 px-2 py-0.5 text-xs font-medium text-purple-800">
                    {r.algorithm}
                  </span>
                </td>
                <td className="px-4 py-3 text-sm text-gray-900">{r.totalBookings}</td>
                <td className="px-4 py-3 text-sm text-gray-900">{r.availableSlots}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {totalPages > 1 && (
        <div className="mt-4 flex items-center justify-center gap-2">
          <button onClick={() => setPage((p) => Math.max(1, p - 1))} disabled={page <= 1}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm disabled:opacity-50">Previous</button>
          <span className="text-sm text-gray-600">Page {page} of {totalPages}</span>
          <button onClick={() => setPage((p) => Math.min(totalPages, p + 1))} disabled={page >= totalPages}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm disabled:opacity-50">Next</button>
        </div>
      )}
    </div>
  );
}
