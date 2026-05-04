import { useEffect, useState } from 'react';
import { adminService } from '../../services/adminService';
import type { AdminLocation, AdminSlot } from '../../types/admin';

export function SlotsPage() {
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [selectedLocationId, setSelectedLocationId] = useState<string | null>(null);
  const [slots, setSlots] = useState<AdminSlot[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formSlotNumber, setFormSlotNumber] = useState('');
  const [formLabel, setFormLabel] = useState('');
  const [formIsActive, setFormIsActive] = useState(true);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    adminService.getLocations()
      .then((locs) => { setLocations(locs); if (locs.length > 0) setSelectedLocationId(locs[0].id); })
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
        await adminService.updateSlot(editingId, { slotNumber: formSlotNumber, label: formLabel || undefined, isActive: formIsActive });
      } else {
        await adminService.createSlot({ locationId: selectedLocationId, slotNumber: formSlotNumber, label: formLabel || undefined });
      }
      setModalOpen(false);
      setSlots(await adminService.getSlots(selectedLocationId));
    } catch { setError('Failed to save slot.'); }
    finally { setSaving(false); }
  };

  const handleDeactivate = async (id: string) => {
    if (!selectedLocationId) return;
    try {
      await adminService.deactivateSlot(id);
      setSlots(await adminService.getSlots(selectedLocationId));
    } catch { setError('Failed to deactivate slot.'); }
  };

  if (loading) return <div className="flex h-64 items-center justify-center text-gray-400">Loading...</div>;

  return (
    <div>
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">Parking Slots</h1>
        <button onClick={openCreate} disabled={!selectedLocationId}
          className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
          New Slot
        </button>
      </div>

      {error && <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}

      <div className="mt-4">
        <label htmlFor="slots-location" className="block text-sm font-medium text-gray-700">Location</label>
        <select id="slots-location" value={selectedLocationId ?? ''} onChange={(e) => setSelectedLocationId(e.target.value)}
          className="mt-1 rounded-md border border-gray-300 px-3 py-2 text-sm">
          {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
        </select>
      </div>

      <div className="mt-6 overflow-hidden rounded-lg border border-gray-200">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Slot Number</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Label</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Status</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 bg-white">
            {slots.map((slot) => (
              <tr key={slot.id}>
                <td className="px-4 py-3 text-sm font-medium text-gray-900">{slot.slotNumber}</td>
                <td className="px-4 py-3 text-sm text-gray-500">{slot.label ?? '—'}</td>
                <td className="px-4 py-3">
                  <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${slot.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-500'}`}>
                    {slot.isActive ? 'Active' : 'Inactive'}
                  </span>
                </td>
                <td className="px-4 py-3 text-sm">
                  <div className="flex gap-2">
                    <button onClick={() => openEdit(slot)} className="text-blue-600 hover:text-blue-800">Edit</button>
                    {slot.isActive && (
                      <button onClick={() => handleDeactivate(slot.id)} className="text-red-600 hover:text-red-800">Deactivate</button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
            {slots.length === 0 && (
              <tr><td colSpan={4} className="px-4 py-8 text-center text-sm text-gray-400">No slots found.</td></tr>
            )}
          </tbody>
        </table>
      </div>

      {modalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="mx-4 w-full max-w-md rounded-lg bg-white p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900">{editingId ? 'Edit Slot' : 'New Slot'}</h3>
            <div className="mt-4 space-y-3">
              <div>
                <label htmlFor="slot-number" className="block text-sm font-medium text-gray-700">Slot Number</label>
                <input id="slot-number" value={formSlotNumber} onChange={(e) => setFormSlotNumber(e.target.value)}
                  className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm" placeholder="e.g. P001" />
              </div>
              <div>
                <label htmlFor="slot-label" className="block text-sm font-medium text-gray-700">Label (optional)</label>
                <input id="slot-label" value={formLabel} onChange={(e) => setFormLabel(e.target.value)}
                  className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm" placeholder="e.g. Near entrance" />
              </div>
              {editingId && (
                <label className="flex items-center gap-2 text-sm">
                  <input type="checkbox" checked={formIsActive} onChange={(e) => setFormIsActive(e.target.checked)} />
                  Active
                </label>
              )}
            </div>
            <div className="mt-4 flex justify-end gap-3">
              <button onClick={() => setModalOpen(false)} className="rounded-md border border-gray-300 px-4 py-2 text-sm text-gray-700 hover:bg-gray-50">Cancel</button>
              <button onClick={handleSave} disabled={saving || !formSlotNumber}
                className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
                {saving ? 'Saving...' : 'Save'}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
