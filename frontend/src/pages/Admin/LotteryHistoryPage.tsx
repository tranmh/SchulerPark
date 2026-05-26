import { useEffect, useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { adminService } from '../../services/adminService';
import type { AdminLocation, LotteryRun } from '../../types/admin';

function algorithmTone(algorithm: string) {
  switch (algorithm) {
    case 'WeightedHistory':
      return 'bg-brand-50 text-brand-800 ring-brand-200';
    case 'PureRandom':
      return 'bg-amber-50 text-amber-800 ring-amber-200';
    case 'RoundRobin':
      return 'bg-violet-50 text-violet-800 ring-violet-200';
    default:
      return 'bg-ink-100 text-ink-700 ring-line';
  }
}

function ratioBar(filled: number, total: number) {
  const pct = total > 0 ? Math.round((filled / total) * 100) : 0;
  const tone =
    pct >= 90 ? 'bg-amber-500' : pct >= 60 ? 'bg-emerald-500' : pct >= 30 ? 'bg-brand-500' : 'bg-rose-500';
  return { pct, tone };
}

export function LotteryHistoryPage() {
  const { t } = useTranslation();
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [runs, setRuns] = useState<LotteryRun[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
    } catch {
      setError('Failed to load lottery runs.');
    } finally {
      setLoading(false);
    }
  }, [locationFilter, page]);

  useEffect(() => {
    loadRuns();
  }, [loadRuns]);

  const totalPages = Math.ceil(totalCount / pageSize);

  return (
    <div>
      <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('admin.lotteryHistory')}</h1>
      <p className="mt-1 text-[13.5px] text-ink-400">
        <span className="font-medium text-ink-700 num">{totalCount}</span> run{totalCount !== 1 ? 's' : ''} in total.
      </p>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">{error}</div>
      )}

      <div className="mt-5">
        <label htmlFor="lottery-location" className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">Location</label>
        <select
          id="lottery-location"
          value={locationFilter}
          onChange={(e) => {
            setLocationFilter(e.target.value);
            setPage(1);
          }}
          className="w-72 rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900"
        >
          <option value="">All locations</option>
          {locations.map((l) => (
            <option key={l.id} value={l.id}>{l.name}</option>
          ))}
        </select>
      </div>

      <div className="mt-6 overflow-hidden rounded-card border border-line bg-white shadow-card">
        <table className="min-w-full num">
          <thead className="bg-surface-warm">
            <tr>
              <Th>Ran at</Th>
              <Th>Location</Th>
              <Th>For date</Th>
              <Th>Time slot</Th>
              <Th>Algorithm</Th>
              <Th>Fill ratio</Th>
              <Th className="text-right pr-6">Bookings · Slots</Th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {loading ? (
              <tr>
                <td colSpan={7} className="px-4 py-10 text-center text-[13px] text-ink-400">Loading…</td>
              </tr>
            ) : runs.length === 0 ? (
              <tr>
                <td colSpan={7} className="px-4 py-10 text-center text-[13px] text-ink-400">No lottery runs found.</td>
              </tr>
            ) : (
              runs.map((r) => {
                const { pct, tone } = ratioBar(r.totalBookings, r.availableSlots);
                return (
                  <tr key={r.id} className="hover:bg-surface-warm/60">
                    <Td>
                      <div className="font-medium text-ink-900">
                        {new Date(r.ranAt).toLocaleString('de-DE', {
                          day: '2-digit',
                          month: '2-digit',
                          year: 'numeric',
                          hour: '2-digit',
                          minute: '2-digit',
                        })}
                      </div>
                    </Td>
                    <Td className="text-ink-700">{r.locationName}</Td>
                    <Td className="text-ink-500">{r.date}</Td>
                    <Td className="text-ink-500">{r.timeSlot}</Td>
                    <Td>
                      <span
                        className={`inline-flex items-center rounded-full px-2.5 py-0.5 text-[11.5px] font-semibold ring-1 ring-inset ${algorithmTone(r.algorithm)}`}
                      >
                        {r.algorithm}
                      </span>
                    </Td>
                    <Td>
                      <div className="flex items-center gap-2">
                        <div className="h-1.5 w-32 overflow-hidden rounded-full bg-line">
                          <div className={`h-full rounded-full ${tone}`} style={{ width: `${pct}%` }} />
                        </div>
                        <span className="text-[12px] font-medium text-ink-700">{pct}%</span>
                      </div>
                    </Td>
                    <Td className="text-right pr-6">
                      <span className="font-semibold text-ink-900">{r.totalBookings}</span>{' '}
                      <span className="text-ink-300">/ {r.availableSlots}</span>
                    </Td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>

      {totalPages > 1 && (
        <div className="mt-5 flex items-center justify-center gap-3">
          <button
            type="button"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page <= 1}
            className="rounded-lg border border-line-strong bg-white px-3 py-1.5 text-[12.5px] font-medium text-ink-700 disabled:opacity-50"
          >
            ← Previous
          </button>
          <span className="text-[12.5px] text-ink-400">
            Page <span className="font-semibold text-ink-700 num">{page}</span> of <span className="num">{totalPages}</span>
          </span>
          <button
            type="button"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
            className="rounded-lg border border-line-strong bg-white px-3 py-1.5 text-[12.5px] font-medium text-ink-700 disabled:opacity-50"
          >
            Next →
          </button>
        </div>
      )}
    </div>
  );
}

function Th({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return (
    <th
      className={`px-4 py-3 text-left text-[11px] font-semibold uppercase tracking-[0.06em] text-ink-400 border-b border-line ${className}`}
    >
      {children}
    </th>
  );
}
function Td({ children, className = '' }: { children: React.ReactNode; className?: string }) {
  return <td className={`px-4 py-3.5 text-[13.5px] text-ink-700 ${className}`}>{children}</td>;
}
