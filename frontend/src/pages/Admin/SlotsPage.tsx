import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminService } from '../../services/adminService';
import { LoadingSpinner } from '../../components/LoadingSpinner';
import type { AdminLocation, AdminSlot } from '../../types/admin';

export function SlotsPage() {
  const { t } = useTranslation();
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [selectedLocationId, setSelectedLocationId] = useState<string | null>(null);
  const [slots, setSlots] = useState<AdminSlot[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formSlotNumber, setFormSlotNumber] = useState('');
  const [formLabel, setFormLabel] = useState('');
  const [formIsActive, setFormIsActive] = useState(true);
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

  useEffect(() => {
    if (!selectedLocationId) return;
    adminService.getSlots(selectedLocationId).then(setSlots).catch(() => setError('Failed to load slots.'));
  }, [selectedLocationId]);

  const openCreate = () => {
    setEditingId(null);
    setFormSlotNumber('');
    setFormLabel('');
    setFormIsActive(true);
    setModalOpen(true);
  };

  const openEdit = (slot: AdminSlot) => {
    setEditingId(slot.id);
    setFormSlotNumber(slot.slotNumber);
    setFormLabel(slot.label ?? '');
    setFormIsActive(slot.isActive);
    setModalOpen(true);
  };

  const handleSave = async () => {
    if (!selectedLocationId) return;
    setSaving(true);
    setError(null);
    try {
      if (editingId) {
        await adminService.updateSlot(editingId, {
          slotNumber: formSlotNumber,
          label: formLabel || undefined,
          isActive: formIsActive,
        });
      } else {
        await adminService.createSlot({
          locationId: selectedLocationId,
          slotNumber: formSlotNumber,
          label: formLabel || undefined,
        });
      }
      setModalOpen(false);
      setSlots(await adminService.getSlots(selectedLocationId));
    } catch {
      setError('Failed to save slot.');
    } finally {
      setSaving(false);
    }
  };

  const handleDeactivate = async (id: string) => {
    if (!selectedLocationId) return;
    try {
      await adminService.deactivateSlot(id);
      setSlots(await adminService.getSlots(selectedLocationId));
    } catch {
      setError('Failed to deactivate slot.');
    }
  };

  if (loading) return <LoadingSpinner />;

  return (
    <div>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('admin.parkingSlots')}</h1>
          <p className="mt-1 text-[13.5px] text-ink-400">Add, edit and deactivate individual parking slots per location.</p>
        </div>
        <button
          type="button"
          onClick={openCreate}
          disabled={!selectedLocationId}
          className="inline-flex items-center gap-2 rounded-lg bg-brand-500 px-4 py-2.5 text-[13.5px] font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-50"
        >
          <svg className="h-4 w-4" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 4v16m8-8H4" />
          </svg>
          New slot
        </button>
      </div>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
          {error}
        </div>
      )}

      <div className="mt-6">
        <label htmlFor="slots-location" className="mb-1.5 block text-[12.5px] font-medium text-ink-500">Location</label>
        <select
          id="slots-location"
          value={selectedLocationId ?? ''}
          onChange={(e) => setSelectedLocationId(e.target.value)}
          className="w-72 rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
        >
          {locations.map((l) => (
            <option key={l.id} value={l.id}>{l.name}</option>
          ))}
        </select>
      </div>

      <div className="mt-6 overflow-hidden rounded-card border border-line bg-white shadow-card">
        <table className="min-w-full num">
          <thead className="bg-surface-warm">
            <tr>
              <Th>Slot number</Th>
              <Th>Label</Th>
              <Th>Status</Th>
              <Th className="text-right pr-6">Actions</Th>
            </tr>
          </thead>
          <tbody className="divide-y divide-line">
            {slots.length === 0 ? (
              <tr>
                <td colSpan={4} className="px-4 py-10 text-center text-[13px] text-ink-400">
                  No slots configured for this location.
                </td>
              </tr>
            ) : (
              slots.map((slot) => (
                <tr key={slot.id} className="hover:bg-surface-warm/60">
                  <Td className="font-semibold text-ink-900">{slot.slotNumber}</Td>
                  <Td className="text-ink-500">{slot.label ?? <span className="text-ink-300">—</span>}</Td>
                  <Td>
                    {slot.isActive ? (
                      <Pill tone="active">Active</Pill>
                    ) : (
                      <Pill tone="inactive">Inactive</Pill>
                    )}
                  </Td>
                  <Td className="text-right pr-6">
                    <button
                      type="button"
                      onClick={() => openEdit(slot)}
                      className="text-[12.5px] font-medium text-brand-500 hover:text-brand-700"
                    >
                      Edit
                    </button>
                    {slot.isActive && (
                      <>
                        <span className="mx-2 text-line">·</span>
                        <button
                          type="button"
                          onClick={() => handleDeactivate(slot.id)}
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
        <Modal title={editingId ? 'Edit slot' : 'New slot'} onClose={() => setModalOpen(false)}>
          <div className="space-y-4">
            <FormField label="Slot number" htmlFor="slot-number">
              <input
                id="slot-number"
                value={formSlotNumber}
                onChange={(e) => setFormSlotNumber(e.target.value)}
                placeholder="e.g. P001"
                className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900 num"
              />
            </FormField>
            <FormField label="Label (optional)" htmlFor="slot-label">
              <input
                id="slot-label"
                value={formLabel}
                onChange={(e) => setFormLabel(e.target.value)}
                placeholder="e.g. Near entrance"
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
              disabled={saving || !formSlotNumber}
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
function Pill({ tone, children }: { tone: 'active' | 'inactive'; children: React.ReactNode }) {
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
