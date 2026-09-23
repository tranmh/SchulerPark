import { useEffect, useState } from 'react';
import { Modal } from '../../components/Modal';
import { useTranslation } from 'react-i18next';
import { adminService } from '../../services/adminService';
import { LoadingSpinner } from '../../components/LoadingSpinner';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { CapacityImpactBanner } from '../../components/CapacityImpactBanner';
import { todayInBerlin } from '../../utils/berlinTime';
import type { AdminLocation, CapacityChangeResult } from '../../types/admin';

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

  // WP1 3.4: deactivation previews how many live bookings the location holds and
  // reports what happened to them afterwards.
  const [deactivateTarget, setDeactivateTarget] = useState<AdminLocation | null>(null);
  const [affectedCount, setAffectedCount] = useState<number | null>(null);
  const [deactivating, setDeactivating] = useState(false);
  const [impact, setImpact] = useState<CapacityChangeResult | null>(null);

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

  const openDeactivate = (loc: AdminLocation) => {
    setDeactivateTarget(loc);
    setAffectedCount(null);
    setImpact(null);
    adminService
      .countBookings({ locationId: loc.id, from: todayInBerlin(), status: 'Pending,Won,Confirmed,Lost' })
      .then(setAffectedCount)
      .catch(() => setAffectedCount(0));
  };

  const handleDeactivate = async () => {
    if (!deactivateTarget) return;
    setDeactivating(true);
    setError(null);
    try {
      const result = await adminService.deactivateLocation(deactivateTarget.id);
      setImpact(result);
      setDeactivateTarget(null);
      await load();
    } catch {
      setError('Failed to deactivate location.');
    } finally {
      setDeactivating(false);
    }
  };

  if (loading) return <LoadingSpinner />;

  const deactivateMessage = deactivateTarget
    ? [
        t('admin.capacityImpact.deactivateLocationMessage', { location: deactivateTarget.name }),
        affectedCount === null
          ? t('admin.capacityImpact.checking')
          : affectedCount === 0
            ? t('admin.capacityImpact.none')
            : t('admin.capacityImpact.locationAffected', { count: affectedCount }),
      ].join(' ')
    : '';

  return (
    <div>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('admin.locations')}</h1>
          <p className="mt-1 text-[13.5px] text-ink-400">Manage parking sites, slot capacity and lottery algorithm per site.</p>
        </div>
        <button
          type="button"
          onClick={openCreate}
          className="inline-flex min-h-11 w-full items-center justify-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 text-[13.5px] font-medium text-white shadow-sm hover:bg-brand-600 sm:min-h-0 sm:w-auto"
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

      <CapacityImpactBanner impact={impact} onDismiss={() => setImpact(null)} />

      <div className="mt-7 overflow-x-auto rounded-card border border-line bg-white shadow-card">
        <table className="w-full min-w-[640px] num">
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
                          onClick={() => openDeactivate(loc)}
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

      <ConfirmDialog
        isOpen={!!deactivateTarget}
        title={t('admin.capacityImpact.deactivateLocationTitle')}
        message={deactivateMessage}
        confirmLabel={t('admin.capacityImpact.deactivate')}
        onConfirm={handleDeactivate}
        onCancel={() => setDeactivateTarget(null)}
        isLoading={deactivating}
      />

      {modalOpen && (
        <Modal title={editingId ? 'Edit location' : 'New location'} onClose={() => setModalOpen(false)}>
          <div className="space-y-4">
            <FormField label="Name" htmlFor="location-name">
              <input
                id="location-name"
                value={formName}
                onChange={(e) => setFormName(e.target.value)}
                className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
              />
            </FormField>
            <FormField label="Address" htmlFor="location-address">
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

function FormField({ label, htmlFor, children }: { label: string; htmlFor?: string; children: React.ReactNode }) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1.5 block text-[12.5px] font-medium text-ink-500">{label}</label>
      {children}
    </div>
  );
}
