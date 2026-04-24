import { useEffect, useState } from 'react';
import { useAuth } from '../../contexts/AuthContext';
import { profileService } from '../../services/profileService';
import { locationService } from '../../services/locationService';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { usePushNotifications } from '../../hooks/usePushNotifications';
import type { Location, ParkingSlot } from '../../types/booking';

export function ProfilePage() {
  const { user, logout } = useAuth();
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [carLicensePlate, setCarLicensePlate] = useState(user?.carLicensePlate ?? '');
  const [preferredLocationId, setPreferredLocationId] = useState<string | null>(user?.preferredLocationId ?? null);
  const [preferredSlotId, setPreferredSlotId] = useState<string | null>(user?.preferredSlotId ?? null);
  const [locations, setLocations] = useState<Location[]>([]);
  const [slots, setSlots] = useState<ParkingSlot[]>([]);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Deletion
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [deleting, setDeleting] = useState(false);

  const { isSupported: pushSupported, permission: pushPermission, isSubscribed: pushSubscribed,
    requestPermission: requestPush, disable: disablePush } = usePushNotifications();

  useEffect(() => {
    if (user) {
      setDisplayName(user.displayName);
      setCarLicensePlate(user.carLicensePlate ?? '');
      setPreferredLocationId(user.preferredLocationId ?? null);
      setPreferredSlotId(user.preferredSlotId ?? null);
    }
  }, [user]);

  useEffect(() => {
    locationService.getLocations()
      .then(setLocations)
      .catch(() => { /* non-fatal: just hide the dropdown */ });
  }, []);

  useEffect(() => {
    if (!preferredLocationId) {
      setSlots([]);
      return;
    }
    locationService.getSlots(preferredLocationId)
      .then((all) => setSlots(all.filter((s) => s.isActive)))
      .catch(() => setSlots([]));
  }, [preferredLocationId]);

  const handleSave = async () => {
    setSaving(true);
    setSaved(false);
    setError(null);
    try {
      await profileService.updateProfile({
        displayName,
        carLicensePlate: carLicensePlate || null,
        preferredLocationId,
        preferredSlotId: preferredLocationId ? preferredSlotId : null,
      });
      setSaved(true);
    } catch { setError('Failed to update profile.'); }
    finally { setSaving(false); }
  };

  const handleExport = async () => {
    try {
      const data = await profileService.exportData();
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `schulerpark-data-export-${new Date().toISOString().split('T')[0]}.json`;
      a.click();
      URL.revokeObjectURL(url);
    } catch { setError('Failed to export data.'); }
  };

  const handleDelete = async () => {
    setDeleting(true);
    try {
      await profileService.requestDeletion();
      setShowDeleteDialog(false);
      await logout();
    } catch { setError('Failed to request account deletion.'); }
    finally { setDeleting(false); }
  };

  return (
    <div className="max-w-2xl">
      <h1 className="text-2xl font-bold text-gray-900">Profile</h1>
      <p className="mt-1 text-sm text-gray-500">Manage your account settings and data.</p>

      {error && <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}
      {saved && <div className="mt-4 rounded-md bg-green-50 px-4 py-3 text-sm text-green-700">Profile updated successfully.</div>}

      {/* Profile form */}
      <div className="mt-6 rounded-lg border border-gray-200 bg-white p-6">
        <h2 className="text-lg font-semibold text-gray-900">Personal Information</h2>
        <div className="mt-4 space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700">Email</label>
            <input value={user?.email ?? ''} disabled
              className="mt-1 w-full rounded-md border border-gray-200 bg-gray-50 px-3 py-2 text-sm text-gray-500" />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Display Name</label>
            <input value={displayName} onChange={(e) => setDisplayName(e.target.value)}
              className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
          </div>
          <div>
            <label className="block text-sm font-medium text-gray-700">Car License Plate</label>
            <input value={carLicensePlate} onChange={(e) => setCarLicensePlate(e.target.value)}
              placeholder="e.g. GP-AB 1234"
              className="mt-1 w-full rounded-md border border-gray-300 px-3 py-2 text-sm" />
          </div>
          <div>
            <label htmlFor="preferredLocation" className="block text-sm font-medium text-gray-700">Preferred Parking Location</label>
            <select
              id="preferredLocation"
              value={preferredLocationId ?? ''}
              onChange={(e) => {
                const next = e.target.value || null;
                setPreferredLocationId(next);
                setPreferredSlotId(null);
              }}
              className="mt-1 w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm"
            >
              <option value="">No preference</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>{l.name}</option>
              ))}
            </select>
            <p className="mt-1 text-xs text-gray-500">
              New bookings will use this location by default. If it's unavailable on a given day, the system will book a random alternative location and notify you.
            </p>
          </div>
          <div>
            <label htmlFor="preferredSlot" className="block text-sm font-medium text-gray-700">Preferred Parking Slot</label>
            <select
              id="preferredSlot"
              value={preferredSlotId ?? ''}
              onChange={(e) => setPreferredSlotId(e.target.value || null)}
              disabled={!preferredLocationId || slots.length === 0}
              className="mt-1 w-full rounded-md border border-gray-300 bg-white px-3 py-2 text-sm disabled:bg-gray-50 disabled:text-gray-400"
            >
              <option value="">No preference</option>
              {slots.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.label ? `${s.slotNumber} — ${s.label}` : s.slotNumber}
                </option>
              ))}
            </select>
            <p className="mt-1 text-xs text-gray-500">
              When you win the daily lottery, you'll get this exact slot if it's free; otherwise the system picks the nearest available slot on the grid. Select a preferred location first.
            </p>
          </div>
          <button onClick={handleSave} disabled={saving || !displayName}
            className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50">
            {saving ? 'Saving...' : 'Save Changes'}
          </button>
        </div>
      </div>

      {/* Data Export */}
      <div className="mt-6 rounded-lg border border-gray-200 bg-white p-6">
        <h2 className="text-lg font-semibold text-gray-900">Your Data (DSGVO Art. 15)</h2>
        <p className="mt-1 text-sm text-gray-500">Download all your personal data as a JSON file.</p>
        <button onClick={handleExport}
          className="mt-4 rounded-md border border-gray-300 px-4 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50">
          Download My Data
        </button>
      </div>

      {/* Push Notifications */}
      {pushSupported && (
        <div className="mt-6 rounded-lg border border-gray-200 bg-white p-6">
          <h2 className="text-lg font-semibold text-gray-900">Push Notifications</h2>
          <p className="mt-1 text-sm text-gray-500">Get notified when lottery results are ready.</p>
          <div className="mt-4">
            {pushPermission === 'denied' ? (
              <p className="text-sm text-red-600">Push notifications are blocked. Please enable them in your browser settings.</p>
            ) : pushSubscribed ? (
              <div className="flex items-center gap-3">
                <span className="inline-flex items-center gap-1.5 text-sm text-green-700">
                  <span className="h-2 w-2 rounded-full bg-green-500" />
                  Push notifications enabled
                </span>
                <button onClick={disablePush}
                  className="rounded-md border border-gray-300 px-3 py-1.5 text-xs font-medium text-gray-700 hover:bg-gray-50">
                  Disable
                </button>
              </div>
            ) : (
              <button onClick={requestPush}
                className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700">
                Enable Push Notifications
              </button>
            )}
          </div>
        </div>
      )}

      {/* Account Deletion */}
      <div className="mt-6 rounded-lg border border-red-200 bg-red-50 p-6">
        <h2 className="text-lg font-semibold text-red-900">Delete Account (DSGVO Art. 17)</h2>
        <p className="mt-1 text-sm text-red-700">
          Your account will be deactivated immediately. All personal data will be permanently deleted after a 30-day grace period.
          This action cannot be undone.
        </p>
        <button onClick={() => setShowDeleteDialog(true)}
          className="mt-4 rounded-md bg-red-600 px-4 py-2 text-sm font-medium text-white hover:bg-red-700">
          Delete My Account
        </button>
      </div>

      <ConfirmDialog
        isOpen={showDeleteDialog}
        title="Delete Account"
        message="Are you sure you want to delete your account? Your account will be deactivated immediately and all data permanently deleted after 30 days. This cannot be undone."
        confirmLabel="Delete Account"
        cancelLabel="Keep Account"
        onConfirm={handleDelete}
        onCancel={() => setShowDeleteDialog(false)}
        isLoading={deleting}
      />
    </div>
  );
}
