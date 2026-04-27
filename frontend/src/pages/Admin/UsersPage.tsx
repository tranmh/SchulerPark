import { useCallback, useEffect, useState } from 'react';
import { useAuth } from '../../contexts/AuthContext';
import {
  adminUsersService,
  type AdminUser,
  type UserRole,
} from '../../services/adminUsersService';

const ROLE_OPTIONS: ReadonlyArray<UserRole | ''> = ['', 'User', 'Admin', 'SuperAdmin'];

export function UsersPage() {
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
      setError('Failed to load users.');
    } finally {
      setLoading(false);
    }
  }, [search, roleFilter, page]);

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
      setError(extractError(e, 'Failed to change role.'));
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
      setError(extractError(e, 'Failed to update user.'));
    } finally {
      setBusyId(null);
    }
  };

  const handleDelete = async (u: AdminUser) => {
    if (!window.confirm(`Permanently delete ${u.email}? This cannot be undone.`)) return;
    setBusyId(u.id);
    setError(null);
    try {
      await adminUsersService.remove(u.id);
      await load();
    } catch (e) {
      setError(extractError(e, 'Failed to delete user.'));
    } finally {
      setBusyId(null);
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Users</h1>
      <p className="mt-1 text-sm text-gray-500">
        {totalCount} user{totalCount !== 1 ? 's' : ''} · changes take effect on the affected user&rsquo;s next login.
      </p>

      {error && (
        <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>
      )}

      {/* Filters */}
      <div className="mt-4 flex flex-wrap gap-3">
        <input
          type="text"
          value={search}
          onChange={(e) => {
            setSearch(e.target.value);
            setPage(1);
          }}
          placeholder="Search email or name…"
          className="w-64 rounded-md border border-gray-300 px-3 py-2 text-sm"
        />
        <select
          value={roleFilter}
          onChange={(e) => {
            setRoleFilter(e.target.value as UserRole | '');
            setPage(1);
          }}
          className="rounded-md border border-gray-300 px-3 py-2 text-sm"
        >
          {ROLE_OPTIONS.map((r) => (
            <option key={r || 'all'} value={r}>
              {r === '' ? 'All roles' : r}
            </option>
          ))}
        </select>
      </div>

      {/* Table */}
      <div className="mt-6 overflow-hidden rounded-lg border border-gray-200">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">User</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Role</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Status</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Auth</th>
              <th className="px-4 py-3 text-right text-xs font-medium uppercase text-gray-500">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 bg-white">
            {loading ? (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-sm text-gray-400">
                  Loading…
                </td>
              </tr>
            ) : users.length === 0 ? (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-sm text-gray-400">
                  No users found.
                </td>
              </tr>
            ) : (
              users.map((u) => {
                const isSelf = u.id === currentUser?.id;
                const busy = busyId === u.id;
                return (
                  <tr key={u.id} className={u.isDisabled ? 'bg-gray-50' : undefined}>
                    <td className="px-4 py-3 text-sm">
                      <div className="text-gray-900">{u.displayName}</div>
                      <div className="text-xs text-gray-400">{u.email}</div>
                    </td>
                    <td className="px-4 py-3 text-sm">
                      <select
                        value={u.role}
                        disabled={isSelf || busy}
                        onChange={(e) => handleRoleChange(u, e.target.value as UserRole)}
                        className="rounded-md border border-gray-300 px-2 py-1 text-sm disabled:bg-gray-100 disabled:text-gray-400"
                      >
                        <option value="User">User</option>
                        <option value="Admin">Admin</option>
                        <option value="SuperAdmin">SuperAdmin</option>
                      </select>
                    </td>
                    <td className="px-4 py-3 text-sm">
                      {u.isDisabled ? (
                        <span className="rounded bg-gray-200 px-2 py-0.5 text-xs font-medium text-gray-700">
                          Disabled
                        </span>
                      ) : (
                        <span className="rounded bg-green-100 px-2 py-0.5 text-xs font-medium text-green-800">
                          Active
                        </span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-xs text-gray-500">
                      {u.hasAzureAd ? 'Azure AD' : 'Local'}
                    </td>
                    <td className="px-4 py-3 text-right text-sm">
                      <div className="flex justify-end gap-2">
                        <button
                          type="button"
                          onClick={() => handleToggleDisabled(u)}
                          disabled={isSelf || busy}
                          className="rounded-md border border-gray-300 px-2 py-1 text-xs hover:bg-gray-50 disabled:opacity-40"
                        >
                          {u.isDisabled ? 'Enable' : 'Disable'}
                        </button>
                        <button
                          type="button"
                          onClick={() => handleDelete(u)}
                          disabled={isSelf || busy}
                          className="rounded-md border border-red-300 px-2 py-1 text-xs text-red-700 hover:bg-red-50 disabled:opacity-40"
                        >
                          Delete
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

      {totalPages > 1 && (
        <div className="mt-4 flex items-center justify-center gap-2">
          <button
            type="button"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page <= 1}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm disabled:opacity-50"
          >
            Previous
          </button>
          <span className="text-sm text-gray-600">
            Page {page} of {totalPages}
          </span>
          <button
            type="button"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
            className="rounded-md border border-gray-300 px-3 py-1.5 text-sm disabled:opacity-50"
          >
            Next
          </button>
        </div>
      )}
    </div>
  );
}

function extractError(err: unknown, fallback: string): string {
  if (
    typeof err === 'object' &&
    err !== null &&
    'response' in err &&
    typeof (err as { response?: unknown }).response === 'object'
  ) {
    const data = (err as { response?: { data?: unknown } }).response?.data;
    if (data && typeof data === 'object') {
      const detail = (data as { detail?: unknown; title?: unknown }).detail
        ?? (data as { detail?: unknown; title?: unknown }).title;
      if (typeof detail === 'string' && detail.length > 0) return detail;
    }
  }
  return fallback;
}
