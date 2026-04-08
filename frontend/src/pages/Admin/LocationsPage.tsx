import { useEffect, useState } from 'react';
import { adminService } from '../../services/adminService';
import type { AdminLocation } from '../../types/admin';

const ALGORITHMS = ['PureRandom', 'WeightedHistory', 'RoundRobin'];

export function LocationsPage() {
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Modal state
  const [modalOpen, setModalOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [formName, setFormName] = useState('');
  const [formAddress, setFormAddress] = useState('');
  const [formIsActive, setFormIsActive] = useState(true);
  const [saving, setSaving] = useState(false);

  const load = async () => {
    try {
      setLocations(await adminService.getLocations());
    } catch { setError('Failed to load locations.'); }
    finally { setLoading(false); }
  };

  useEffect(() => { load(); }, []);

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
        await adminService.updateLocation(editingId, { name: formName, address: formAddress, isActive: formIsActive });
      } else {
        await adminService.createLocation({ name: formName, address: formAddress });
      }
      setModalOpen(false);
      await load();
    } catch { setError('Failed to save location.'); }
    finally { setSaving(false); }
  };

  const handleAlgorithmChange = async (id: string, algorithm: string) => {
    try {
      await adminService.setAlgorithm(id, algorithm);
      await load();
    } catch { setError('Failed to update algorithm.'); }
  };

  const handleDeactivate = async (id: string) => {
    try {
      await adminService.deactivateLocation(id);
      await load();
    } catch { setError('Failed to deactivate location.'); }
  };

  if (loading) return <div className="flex h-64 items-center justify-center text-gray-400">Loading...</div>;

  return (
    <div>
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold text-gray-900">Locations</h1>
        <button onClick={openCreate} className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700">
          New Location
        </button>
      </div>

      {error && <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}

      <div className="mt-6 overflow-hidden rounded-lg border border-gray-200">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Name</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Address</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Status</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Slots</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Algorithm</th>
              <th className="px-4 py-3 text-left text-xs font-medium uppercase text-gray-500">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-gray-200 bg-white">
            {locations.map((loc) => (
              <tr key={loc.id}>
                <td className="px-4 py-3 text-sm font-medium text-gray-900">{loc.name}</td>
                <td className="px-4 py-3 text-sm text-gray-500">{loc.address}</td>
                <td className="px-4 py-3">
                  <span className={`inline-flex rounded-full px-2 py-0.5 text-xs font-medium ${loc.isActive ? 'bg-green-100 text-green-800' : 'bg-gray-100 text-gray-500'}`}>
                    {loc.isActive ? 'Active' : 'Inactive'}
                  </span>
                </td>
                <td className="px-4 py-3 text-sm text-gray-500">{loc.activeSlots}/{loc.totalSlots}</td>
                <td className="px-4 py-3">
                  <select
                    value={loc.defaultAlgorithm}
                    onChange={(e) => handleAlgorithmChange(loc.id, e.target.value)}
                    className="rounded border border-gray-300 px-2 py-1 text-sm"
                  >
                    {ALGORITHMS.map((a) => <option key={a} value={a}>{a}</option>)}
                  </select>
                </td>
                <td className="px-4 py-3 text-sm">
                  <div className="flex gap-2">
                    <button onClick={() => openEdit(loc)} className="text-blue-600 hover:text-blue-800">Edit</button>
                    {loc.isActive && (
                      <button onClick={() => handleDeactivate(loc.id)} className="text-red-600 hover:text-red-800">Deactivate</button>
                    )}
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Create/Edit Modal */}
      {modalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50">
          <div className="mx-4 w-full max-w-md rounded-lg bg-white p-6 shadow-xl">
            <h3 className="text-lg font-semibold text-gray-900">{editingId ? 'Edit Location' : 'New Location'}</h3>
            <div className="mt-4 space-y-3">
              <div>
                <label className="block text-sm font-medium text-gray-700">Name</label>
                <input value={formName} onChange={(e) => setFormName(e.target.value)}
                  className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700">Address</label>
                <input value={formAddress} onChange={(e) => setFormAddress(e.target.value)}
                  className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
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
              <button onClick={handleSave} disabled={saving || !formName || !formAddress}
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
