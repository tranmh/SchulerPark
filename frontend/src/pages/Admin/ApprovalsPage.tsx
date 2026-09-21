import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminUsersService, type PendingUser } from '../../services/adminUsersService';

export function ApprovalsPage() {
  const { t } = useTranslation();
  const [users, setUsers] = useState<PendingUser[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await adminUsersService.pending();
      setUsers(res.users);
    } catch {
      setError(t('approvals.loadFailed'));
    } finally {
      setLoading(false);
    }
  }, [t]);

  useEffect(() => {
    load();
  }, [load]);

  const handleDecision = async (u: PendingUser, approve: boolean) => {
    if (!approve && !window.confirm(t('approvals.rejectConfirm', { email: u.email }))) return;
    setBusyId(u.id);
    setError(null);
    try {
      await adminUsersService.decide(u.id, approve);
      await load();
    } catch {
      setError(t('approvals.decisionFailed'));
    } finally {
      setBusyId(null);
    }
  };

  return (
    <div>
      <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('approvals.title')}</h1>
      <p className="mt-1 text-[13.5px] text-ink-400">{t('approvals.subtitle')}</p>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">{error}</div>
      )}

      <div className="mt-6 overflow-x-auto rounded-card border border-line bg-white shadow-card">
        <table className="w-full min-w-[640px]">
          <thead className="bg-surface-warm">
            <tr>
              <th className="px-4 py-3 text-left text-[11px] font-semibold uppercase tracking-[0.06em] text-ink-400 border-b border-line">{t('approvals.user')}</th>
              <th className="px-4 py-3 text-left text-[11px] font-semibold uppercase tracking-[0.06em] text-ink-400 border-b border-line">{t('approvals.registered')}</th>
              <th className="px-4 py-3 text-left text-[11px] font-semibold uppercase tracking-[0.06em] text-ink-400 border-b border-line">{t('approvals.emailStatus')}</th>
              <th className="px-4 py-3 text-right pr-6 text-[11px] font-semibold uppercase tracking-[0.06em] text-ink-400 border-b border-line">{t('approvals.actions')}</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {loading ? (
              <tr>
                <td colSpan={4} className="px-4 py-10 text-center text-[13px] text-ink-400">{t('common.loading')}</td>
              </tr>
            ) : users.length === 0 ? (
              <tr>
                <td colSpan={4} className="px-4 py-10 text-center text-[13px] text-ink-400">{t('approvals.empty')}</td>
              </tr>
            ) : (
              users.map((u) => {
                const busy = busyId === u.id;
                return (
                  <tr key={u.id} className="hover:bg-surface-warm/60">
                    <td className="px-4 py-3.5">
                      <div className="min-w-0">
                        <div className="truncate text-[13.5px] font-medium text-ink-900">{u.displayName}</div>
                        <div className="truncate text-[11.5px] text-ink-400">{u.email}</div>
                      </div>
                    </td>
                    <td className="px-4 py-3.5 text-[13px] text-ink-500">
                      {new Date(u.createdAt).toLocaleDateString()}
                    </td>
                    <td className="px-4 py-3.5">
                      {u.emailVerified ? (
                        <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 py-0.5 text-[11.5px] font-semibold text-emerald-800 ring-1 ring-inset ring-emerald-200">
                          <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                          {t('approvals.verified')}
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-50 px-2.5 py-0.5 text-[11.5px] font-semibold text-amber-800 ring-1 ring-inset ring-amber-200">
                          <span className="h-1.5 w-1.5 rounded-full bg-amber-500" />
                          {t('approvals.unverified')}
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3.5 text-right pr-6">
                      <div className="flex justify-end gap-2">
                        <button
                          type="button"
                          onClick={() => handleDecision(u, true)}
                          disabled={busy}
                          className="rounded-md bg-emerald-600 px-3 py-1.5 text-[11.5px] font-medium text-white hover:bg-emerald-700 disabled:opacity-40"
                        >
                          {t('approvals.approve')}
                        </button>
                        <button
                          type="button"
                          onClick={() => handleDecision(u, false)}
                          disabled={busy}
                          className="rounded-md border border-rose-200 bg-white px-3 py-1.5 text-[11.5px] font-medium text-rose-700 hover:bg-rose-50 disabled:opacity-40"
                        >
                          {t('approvals.reject')}
                        </button>
                      </div>
                    </td>
                  </tr>
                );
              })
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
