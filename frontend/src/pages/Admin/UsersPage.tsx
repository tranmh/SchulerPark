import { useCallback, useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../contexts/AuthContext';
import { adminUsersService, type AdminUser, type UserRole } from '../../services/adminUsersService';
import { describeApiError } from '../../utils/apiError';

const ROLE_OPTIONS: ReadonlyArray<UserRole | ''> = ['', 'User', 'Admin', 'SuperAdmin'];

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('') || 'U';
}

function roleTone(role: UserRole) {
  switch (role) {
    case 'SuperAdmin':
      return 'bg-violet-50 text-violet-800 ring-violet-200';
    case 'Admin':
      return 'bg-amber-50 text-amber-800 ring-amber-200';
    default:
      return 'bg-ink-100 text-ink-700 ring-line';
  }
}

export function UsersPage() {
  const { t } = useTranslation();
  const { user: currentUser } = useAuth();
  const [users, setUsers] = useState<AdminUser[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [roleFilter, setRoleFilter] = useState<UserRole | ''>('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const pageSize = 20;

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await adminUsersService.list({
        search: search || undefined,
        role: roleFilter || undefined,
        page,
        pageSize,
      });
      setUsers(res.users);
      setTotalCount(res.totalCount);
    } catch {
      setError(t('admin.usersPage.loadFailed'));
    } finally {
      setLoading(false);
    }
  }, [search, roleFilter, page, t]);

  useEffect(() => {
    load();
  }, [load]);

  const handleRoleChange = async (u: AdminUser, role: UserRole) => {
    if (role === u.role) return;
    setBusyId(u.id);
    setError(null);
    try {
      await adminUsersService.updateRole(u.id, role);
      await load();
    } catch (e) {
      setError(describeApiError(e, 'admin.usersPage.roleFailed'));
    } finally {
      setBusyId(null);
    }
  };

  const handleToggleDisabled = async (u: AdminUser) => {
    setBusyId(u.id);
    setError(null);
    try {
      if (u.isDisabled) await adminUsersService.enable(u.id);
      else await adminUsersService.disable(u.id);
      await load();
    } catch (e) {
      setError(describeApiError(e, 'admin.usersPage.updateFailed'));
    } finally {
      setBusyId(null);
    }
  };

  const handleDelete = async (u: AdminUser) => {
    if (!window.confirm(t('admin.usersPage.deleteConfirm', { email: u.email }))) return;
    setBusyId(u.id);
    setError(null);
    try {
      await adminUsersService.remove(u.id);
      await load();
    } catch (e) {
      setError(describeApiError(e, 'admin.usersPage.deleteFailed'));
    } finally {
      setBusyId(null);
    }
  };

  // Phase 21: the company is read-only for the user, so this is the only place it changes.
  const handleEditCompany = async (u: AdminUser) => {
    const next = window.prompt(t('admin.usersPage.companyPrompt', { name: u.displayName }), u.company ?? '');
    if (next === null) return;
    const value = next.trim() || null;
    if (value === (u.company ?? null)) return;
    setBusyId(u.id);
    setError(null);
    try {
      await adminUsersService.updateCompany(u.id, value);
      await load();
    } catch (e) {
      setError(describeApiError(e, 'admin.usersPage.updateFailed'));
    } finally {
      setBusyId(null);
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div>
      <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('admin.users')}</h1>
      <p className="mt-1 text-[13.5px] text-ink-400">
        <span className="font-medium text-ink-700 num">{totalCount}</span>{' '}
        {t('admin.usersPage.count', { count: totalCount })} · {t('admin.usersPage.roleHint')}
      </p>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">{error}</div>
      )}

      {/* Filters */}
      <div className="mt-5 flex flex-wrap items-end gap-3 [&>div]:w-full sm:[&>div]:w-auto">
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">{t('admin.usersPage.search')}</label>
          <div className="relative">
            <svg
              className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-300"
              fill="none"
              stroke="currentColor"
              viewBox="0 0 24 24"
            >
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M21 21l-4.35-4.35M11 18a7 7 0 100-14 7 7 0 000 14z" />
            </svg>
            <input
              type="text"
              value={search}
              onChange={(e) => {
                setSearch(e.target.value);
                setPage(1);
              }}
              placeholder={t('admin.usersPage.searchPlaceholder')}
              className="w-full sm:w-72 rounded-lg border border-line-strong bg-white pl-9 pr-3 py-2.5 text-[13px] text-ink-900 placeholder:text-ink-300"
            />
          </div>
        </div>
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">{t('admin.usersPage.role')}</label>
          <select
            value={roleFilter}
            onChange={(e) => {
              setRoleFilter(e.target.value as UserRole | '');
              setPage(1);
            }}
            className="rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900"
          >
            {ROLE_OPTIONS.map((r) => (
              <option key={r || 'all'} value={r}>
                {r === '' ? t('admin.usersPage.allRoles') : r}
              </option>
            ))}
          </select>
        </div>
      </div>

      {/* Table */}
      <div className="mt-6 overflow-x-auto rounded-card border border-line bg-white shadow-card">
        <table className="w-full min-w-[860px]">
          <thead className="bg-surface-warm">
            <tr>
              <Th>{t('admin.usersPage.user')}</Th>
              <Th>{t('admin.usersPage.company')}</Th>
              <Th>{t('admin.usersPage.role')}</Th>
              <Th>{t('admin.usersPage.status')}</Th>
              <Th>{t('admin.usersPage.auth')}</Th>
              <Th className="text-right pr-6">{t('admin.usersPage.actions')}</Th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {loading ? (
              <tr>
                <td colSpan={6} className="px-4 py-10 text-center text-[13px] text-ink-400">{t('common.loading')}</td>
              </tr>
            ) : users.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-4 py-10 text-center text-[13px] text-ink-400">{t('admin.usersPage.empty')}</td>
              </tr>
            ) : (
              users.map((u) => {
                const isSelf = u.id === currentUser?.id;
                const busy = busyId === u.id;
                return (
                  <tr key={u.id} className={`hover:bg-surface-warm/60 ${u.isDisabled ? 'opacity-60' : ''}`}>
                    <Td>
                      <div className="flex items-center gap-3">
                        <div
                          className={`grid h-9 w-9 shrink-0 place-items-center rounded-full text-[12px] font-semibold text-white ${
                            u.role === 'SuperAdmin'
                              ? 'bg-gradient-to-br from-violet-400 to-violet-700'
                              : u.role === 'Admin'
                                ? 'bg-gradient-to-br from-amber-400 to-amber-700'
                                : 'bg-gradient-to-br from-brand-300 to-brand-700'
                          }`}
                        >
                          {initials(u.displayName)}
                        </div>
                        <div className="min-w-0">
                          <div className="truncate text-[13.5px] font-medium text-ink-900">{u.displayName}</div>
                          <div className="truncate text-[11.5px] text-ink-400">{u.email}</div>
                        </div>
                        {isSelf && (
                          <span className="rounded-full bg-brand-50 px-2 py-0.5 text-[10.5px] font-semibold text-brand-700 ring-1 ring-inset ring-brand-200">
                            {t('admin.usersPage.you')}
                          </span>
                        )}
                      </div>
                    </Td>
                    <Td>
                      <div className="flex items-center gap-1.5">
                        {u.company ? (
                          <span className="truncate text-[13px] text-ink-700">{u.company}</span>
                        ) : u.hasAzureAd ? (
                          <span className="text-[12px] italic text-ink-400">{t('admin.usersPage.internalSso')}</span>
                        ) : (
                          <span className="text-ink-300">—</span>
                        )}
                        <button
                          type="button"
                          onClick={() => handleEditCompany(u)}
                          disabled={busy}
                          aria-label={t('admin.usersPage.editCompany')}
                          title={t('admin.usersPage.editCompany')}
                          className="grid h-6 w-6 shrink-0 place-items-center rounded-md text-ink-300 hover:bg-surface-sunken hover:text-ink-700 disabled:opacity-40"
                        >
                          <svg className="h-3.5 w-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M16.862 4.487l1.687-1.688a1.875 1.875 0 112.652 2.652L6.832 19.82a4.5 4.5 0 01-1.897 1.13l-2.685.8.8-2.685a4.5 4.5 0 011.13-1.897L16.863 4.487z" />
                          </svg>
                        </button>
                      </div>
                    </Td>
                    <Td>
                      <select
                        value={u.role}
                        disabled={isSelf || busy}
                        onChange={(e) => handleRoleChange(u, e.target.value as UserRole)}
                        className={`rounded-md border border-line-strong bg-white px-2.5 py-1.5 text-[12.5px] font-medium ring-1 ring-inset ${roleTone(
                          u.role
                        )} disabled:opacity-50`}
                      >
                        <option value="User">User</option>
                        <option value="Admin">Admin</option>
                        <option value="SuperAdmin">SuperAdmin</option>
                      </select>
                    </Td>
                    <Td>
                      {u.isDisabled ? (
                        <span className="inline-flex items-center gap-1.5 rounded-full bg-ink-100 px-2.5 py-0.5 text-[11.5px] font-semibold text-ink-500 ring-1 ring-inset ring-line">
                          <span className="h-1.5 w-1.5 rounded-full bg-ink-300" />
                          {t('admin.usersPage.disabled')}
                        </span>
                      ) : u.approvalStatus === 'Pending' ? (
                        <span className="inline-flex items-center gap-1.5 rounded-full bg-amber-50 px-2.5 py-0.5 text-[11.5px] font-semibold text-amber-800 ring-1 ring-inset ring-amber-200">
                          <span className="h-1.5 w-1.5 rounded-full bg-amber-500" />
                          {t('admin.usersPage.pending')}
                        </span>
                      ) : u.approvalStatus === 'Rejected' ? (
                        <span className="inline-flex items-center gap-1.5 rounded-full bg-rose-50 px-2.5 py-0.5 text-[11.5px] font-semibold text-rose-800 ring-1 ring-inset ring-rose-200">
                          <span className="h-1.5 w-1.5 rounded-full bg-rose-500" />
                          {t('admin.usersPage.rejected')}
                        </span>
                      ) : (
                        <span className="inline-flex items-center gap-1.5 rounded-full bg-emerald-50 px-2.5 py-0.5 text-[11.5px] font-semibold text-emerald-800 ring-1 ring-inset ring-emerald-200">
                          <span className="h-1.5 w-1.5 rounded-full bg-emerald-500" />
                          {t('admin.usersPage.active')}
                        </span>
                      )}
                    </Td>
                    <Td className="text-[12px] text-ink-500">
                      {u.hasAzureAd ? (
                        <span className="inline-flex items-center gap-1.5">
                          <span className="grid h-4 w-4 place-items-center rounded-sm bg-[#0078d4] text-[8px] font-bold text-white">
                            M
                          </span>
                          Azure AD
                        </span>
                      ) : (
                        t('admin.usersPage.local')
                      )}
                    </Td>
                    <Td className="text-right pr-6">
                      <div className="flex justify-end gap-2">
                        <button
                          type="button"
                          onClick={() => handleToggleDisabled(u)}
                          disabled={isSelf || busy}
                          className="rounded-md border border-line-strong bg-white px-2.5 py-1 text-[11.5px] font-medium text-ink-700 hover:bg-surface-sunken disabled:opacity-40"
                        >
                          {u.isDisabled ? t('admin.usersPage.enable') : t('admin.usersPage.disable')}
                        </button>
                        <button
                          type="button"
                          onClick={() => handleDelete(u)}
                          disabled={isSelf || busy}
                          className="rounded-md border border-rose-200 bg-white px-2.5 py-1 text-[11.5px] font-medium text-rose-700 hover:bg-rose-50 disabled:opacity-40"
                        >
                          {t('admin.usersPage.delete')}
                        </button>
                      </div>
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
            ← {t('admin.usersPage.previous')}
          </button>
          <span className="text-[12.5px] text-ink-400">
            {t('admin.usersPage.pageOf', { page, total: totalPages })}
          </span>
          <button
            type="button"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
            className="rounded-lg border border-line-strong bg-white px-3 py-1.5 text-[12.5px] font-medium text-ink-700 disabled:opacity-50"
          >
            {t('admin.usersPage.next')} →
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
