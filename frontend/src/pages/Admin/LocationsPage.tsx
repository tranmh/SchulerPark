import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminService } from '../../services/adminService';
import { LoadingSpinner } from '../../components/LoadingSpinner';
import type { AdminLocation } from '../../types/admin';

const ALGORITHMS = ['PureRandom', 'WeightedHistory', 'RoundRobin'];

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('');
}

export function LocationsPage() {
  const { t } = useTranslation();
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formName, setFormName] = useState('');
  const [formAddress, setFormAddress] = useState('');
  const [formIsActive, setFormIsActive] = useState(true);
  const [saving, setSaving] = useState(false);

  const load = async () => {
    try {
      setLocations(await adminService.getLocations());
    } catch {
      setError('Failed to load locations.');
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const openCreate = () => {
    setEditingId(null);
    setFormName('');
    setFormAddress('');
    setFormIsActive(true);
    setModalOpen(true);
  };

  const openEdit = (loc: AdminLocation) => {
    setEditingId(loc.id);
    setFormName(loc.name);
    setFormAddress(loc.address);
    setFormIsActive(loc.isActive);
    setModalOpen(true);
  };

  const handleSave = async () => {
    setSaving(true);
    setError(null);
    try {
      if (editingId) {
        await adminService.updateLocation(editingId, {
          name: formName,
          address: formAddress,
          isActive: formIsActive,
        });
      } else {
        await adminService.createLocation({ name: formName, address: formAddress });
      }
      setModalOpen(false);
      await load();
    } catch {
      setError('Failed to save location.');
    } finally {
      setSaving(false);
    }
  };

  const handleAlgorithmChange = async (id: string, algorithm: string) => {
    try {
      await adminService.setAlgorithm(id, algorithm);
      await load();
    } catch {
      setError('Failed to update algorithm.');
    }
  };

  const handleDeactivate = async (id: string) => {
    try {
      await adminService.deactivateLocation(id);
      await load();
    } catch {
      setError('Failed to deactivate location.');
    }
  };

  if (loading) return <LoadingSpinner />;

  return (
    <div>
      <div className="flex items-start justify-between">
        <div>
          <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('admin.locations')}</h1>
          <p className="mt-1 text-[13.5px] text-ink-400">Manage parking sites, slot capacity and lottery algorithm per site.</p>
        </div>
        <button
          type="button"
          onClick={openCreate}
          className="inline-flex items-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 text-[13.5px] font-medium text-white shadow-sm hover:bg-brand-600"
        >
          <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          New location
        </button>
      </div>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
          {error}
        </div>
      )}

      <div className="mt-7 overflow-hidden rounded-card border border-line bg-white shadow-card">
        <table className="min-w-full num">
          <thead className="bg-surface-warm">
            <tr>
              <Th>Name</Th>
              <Th>Address</Th>
              <Th>Status</Th>
              <Th>Slots</Th>
              <Th>Lottery algorithm</Th>
              <Th className="text-right pr-6">Actions</Th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {locations.length === 0 ? (
              <tr>
                <td colSpan={6} className="px-4 py-10 text-center text-[13px] text-ink-400">
                  No locations yet. Create your first one above.
                </td>
              </tr>
            ) : (
              locations.map((loc) => (
                <tr key={loc.id} className="hover:bg-surface-warm/60">
                  <Td className="font-semibold text-ink-900">
                    <div className="flex items-center gap-2.5">
                      <div
                        className={`grid h-8 w-8 place-items-center rounded-md text-[11px] font-bold ${
                          loc.isActive ? 'bg-brand-50 text-brand-700' : 'bg-line text-ink-400'
                        }`}
                      >
                        {initials(loc.name)}
                      </div>
                      <span className={loc.isActive ? '' : 'text-ink-500'}>{loc.name}</span>
                    </div>
                  </Td>
                  <Td className="text-ink-400">{loc.address}</Td>
                  <Td>
                    {loc.isActive ? (
                      <Pill tone="active">Active</Pill>
                    ) : (
                      <Pill tone="inactive">Inactive</Pill>
                    )}
                  </Td>
                  <Td>
                    <span className="font-semibold text-ink-900">{loc.activeSlots}</span>{' '}
                    <span className="text-ink-300">/ {loc.totalSlots}</span>
                  </Td>
                  <Td>
                    <select
                      value={loc.defaultAlgorithm}
                      onChange={(e) => handleAlgorithmChange(loc.id, e.target.value)}
                      className="rounded-md border border-line-strong bg-white px-2.5 py-1.5 text-[12.5px] font-medium text-ink-700"
                    >
                      {ALGORITHMS.map((a) => (
                        <option key={a} value={a}>
                          {a}
                        </option>
                      ))}
                    </select>
                  </Td>
                  <Td className="text-right pr-6">
                    <button
                      type="button"
                      onClick={() => openEdit(loc)}
                      className="text-[12.5px] font-medium text-brand-500 hover:text-brand-700"
                    >
                      Edit
                    </button>
                    {loc.isActive && (
                      <>
                        <span className="mx-2 text-line">·</span>
                        <button
                          type="button"
                          onClick={() => handleDeactivate(loc.id)}
                          className="text-[12.5px] font-medium text-rose-600 hover:text-rose-700"
                        >
                          Deactivate
                        </button>
                      </>
                    )}
                  </Td>
                </tr>
              ))
            )}
          </tbody>
        </table>
      </div>

      {modalOpen && (
        <Modal title={editingId ? 'Edit location' : 'New location'} onClose={() => setModalOpen(false)}>
          <div className="space-y-4">
            <FormField label="Name">
              <input
                id="location-name"
                value={formName}
                onChange={(e) => setFormName(e.target.value)}
                className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
              />
            </FormField>
            <FormField label="Address">
              <input
                id="location-address"
                value={formAddress}
                onChange={(e) => setFormAddress(e.target.value)}
                className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
              />
            </FormField>
            {editingId && (
              <label className="flex items-center gap-2 text-[13px] text-ink-700">
                <input
                  type="checkbox"
                  checked={formIsActive}
                  onChange={(e) => setFormIsActive(e.target.checked)}
                  className="h-4 w-4 rounded border-line-strong text-brand-500"
                />
                Active
              </label>
            )}
          </div>
          <div className="mt-5 flex justify-end gap-2 border-t border-line pt-4">
            <button
              type="button"
              onClick={() => setModalOpen(false)}
              className="rounded-lg border border-line-strong bg-white px-4 py-2 text-[13px] font-medium text-ink-700 hover:bg-surface-sunken"
            >
              Cancel
            </button>
            <button
              type="button"
              onClick={handleSave}
              disabled={saving || !formName || !formAddress}
              className="rounded-lg bg-brand-500 px-4 py-2 text-[13px] font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-60"
            >
              {saving ? 'Saving…' : 'Save'}
            </button>
          </div>
        </Modal>
      )}
    </div>
  );
}

/* -------- small shared bits ----------------------------------------- */

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

function Pill({
  tone,
  children,
}: {
  tone: 'active' | 'inactive';
  children: React.ReactNode;
}) {
  const cls =
    tone === 'active'
      ? 'bg-emerald-50 text-emerald-800 ring-emerald-200'
      : 'bg-ink-100 text-ink-500 ring-line';
  const dot = tone === 'active' ? 'bg-emerald-500' : 'bg-ink-300';
  return (
    <span className={`inline-flex items-center gap-1.5 rounded-full px-2.5 py-0.5 text-[11.5px] font-semibold ring-1 ring-inset ${cls}`}>
      <span className={`h-1.5 w-1.5 rounded-full ${dot}`} />
      {children}
    </span>
  );
}

function FormField({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1.5 block text-[12.5px] font-medium text-ink-500">{label}</label>
      {children}
    </div>
  );
}

function Modal({
  title,
  onClose,
  children,
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-ink-900/55 backdrop-blur-[2px] p-4">
      <div className="w-full max-w-md rounded-card bg-white shadow-pop ring-1 ring-line">
        <div className="flex items-center justify-between border-b border-line px-6 py-4">
          <h3 className="text-[15.5px] font-semibold text-ink-900">{title}</h3>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="grid h-7 w-7 place-items-center rounded-md text-ink-400 hover:bg-line/60 hover:text-ink-900"
          >
            <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.8} d="M6 6l12 12M6 18L18 6" />
            </svg>
          </button>
        </div>
        <div className="px-6 py-5">{children}</div>
      </div>
    </div>
  );
}
