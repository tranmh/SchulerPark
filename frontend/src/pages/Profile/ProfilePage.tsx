import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../contexts/AuthContext';
import { profileService } from '../../services/profileService';
import { locationService } from '../../services/locationService';
import { ConfirmDialog } from '../../components/ConfirmDialog';
import { PasswordInput } from '../../components/PasswordInput';
import { usePushNotifications } from '../../hooks/usePushNotifications';
import type { Location, ParkingSlot } from '../../types/booking';
import { describeApiError } from '../../utils/apiError';
import { passwordProblemKey, validatePassword } from '../../utils/passwordRules';

function initials(name: string) {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((p) => p[0]?.toUpperCase() ?? '').join('') || 'U';
}

export function ProfilePage() {
  const { t } = useTranslation();
  const { user, logout, isAdmin, isSuperAdmin } = useAuth();
  const [displayName, setDisplayName] = useState(user?.displayName ?? '');
  const [carLicensePlate, setCarLicensePlate] = useState(user?.carLicensePlate ?? '');
  const [preferredLocationId, setPreferredLocationId] = useState<string | null>(user?.preferredLocationId ?? null);
  const [preferredSlotId, setPreferredSlotId] = useState<string | null>(user?.preferredSlotId ?? null);
  const [locations, setLocations] = useState<Location[]>([]);
  const [slots, setSlots] = useState<ParkingSlot[]>([]);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [deleting, setDeleting] = useState(false);

  // Phase 20 WP2: change password (local accounts only)
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmNewPassword, setConfirmNewPassword] = useState('');
  const [changingPassword, setChangingPassword] = useState(false);
  const [passwordChanged, setPasswordChanged] = useState(false);
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [pushError, setPushError] = useState<string | null>(null);
  const [pushTestState, setPushTestState] = useState<
    | { kind: 'idle' }
    | { kind: 'sending' }
    | { kind: 'ok'; delivered: number }
    | { kind: 'error'; message: string }
  >({ kind: 'idle' });

  const {
    isSupported: pushSupported,
    permission: pushPermission,
    isSubscribed: pushSubscribed,
    requestPermission: requestPush,
    disable: disablePush,
    sendTest: sendTestPush,
  } = usePushNotifications();

  useEffect(() => {
    if (user) {
      setDisplayName(user.displayName);
      setCarLicensePlate(user.carLicensePlate ?? '');
      setPreferredLocationId(user.preferredLocationId ?? null);
      setPreferredSlotId(user.preferredSlotId ?? null);
    }
  }, [user]);

  useEffect(() => {
    locationService.getLocations().then(setLocations).catch(() => {});
  }, []);

  useEffect(() => {
    if (!preferredLocationId) {
      setSlots([]);
      return;
    }
    locationService
      .getSlots(preferredLocationId)
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
    } catch {
      setError(t('profile.updateFailed'));
    } finally {
      setSaving(false);
    }
  };

  const handleChangePassword = async () => {
    setPasswordError(null);
    setPasswordChanged(false);

    const problem = validatePassword(newPassword);
    if (problem) {
      setPasswordError(t(passwordProblemKey(problem)));
      return;
    }
    if (newPassword !== confirmNewPassword) {
      setPasswordError(t('auth.passwordsDoNotMatch'));
      return;
    }

    setChangingPassword(true);
    try {
      // The service installs the fresh access token; every other session is revoked server-side.
      await profileService.changePassword(currentPassword, newPassword);
      setCurrentPassword('');
      setNewPassword('');
      setConfirmNewPassword('');
      setPasswordChanged(true);
    } catch (err: unknown) {
      setPasswordError(describeApiError(err, 'profile.changePassword.failed'));
    } finally {
      setChangingPassword(false);
    }
  };

  const handleExport = async () => {
    try {
      const data = await profileService.exportData();
      const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `louise-data-export-${new Date().toISOString().split('T')[0]}.json`;
      a.click();
      URL.revokeObjectURL(url);
    } catch {
      setError(t('profile.exportFailed'));
    }
  };

  const handleDelete = async () => {
    setDeleting(true);
    try {
      await profileService.requestDeletion();
      setShowDeleteDialog(false);
      await logout();
    } catch {
      setError(t('profile.deletionFailed'));
    } finally {
      setDeleting(false);
    }
  };

  // Bug #11: inspect the discriminated result — show a real error only when the
  // subscribe/permission call actually failed, not when the user simply declined.
  const handleEnablePush = async () => {
    setPushError(null);
    const result = await requestPush();
    if (!result.ok && result.reason === 'error') {
      setPushError(t('profile.pushError'));
    }
  };

  // Manual end-to-end push check (permission → stored subscription → VAPID
  // delivery → service worker). The result is shown inline; the notification
  // itself arrives via the OS.
  const handleTestPush = async () => {
    setPushTestState({ kind: 'sending' });
    const result = await sendTestPush();
    if (result.ok) {
      setPushTestState({ kind: 'ok', delivered: result.delivered });
      return;
    }
    const message =
      result.reason === 'no-subscription'
        ? t('profile.pushTestNoSubscription')
        : result.reason === 'not-delivered'
          ? t('profile.pushTestNotDelivered')
          : t('profile.pushTestFailed');
    setPushTestState({ kind: 'error', message });
  };

  const handleDisablePush = async () => {
    setPushTestState({ kind: 'idle' });
    await disablePush();
  };

  const roleLabel = isSuperAdmin ? t('profile.roleSuper') : isAdmin ? t('profile.roleAdmin') : t('profile.roleUser');

  return (
    <div className="max-w-3xl">
      <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('profile.title')}</h1>
      <p className="mt-1 text-[13.5px] text-ink-400">{t('profile.subtitle')}</p>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
          {error}
        </div>
      )}
      {saved && (
        <div className="mt-5 flex items-start gap-2.5 rounded-lg border border-emerald-200 bg-emerald-50 px-3.5 py-3 text-[13px] text-emerald-800">
          <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
          </svg>
          {t('profile.updatedOk')}
        </div>
      )}

      {/* Identity card */}
      <div className="mt-6 flex flex-wrap items-center gap-5 rounded-card border border-line bg-white p-5 shadow-card">
        <div className="grid h-14 w-14 place-items-center rounded-full bg-gradient-to-br from-brand-300 to-brand-700 text-[20px] font-semibold text-white">
          {initials(user?.displayName ?? '')}
        </div>
        <div className="min-w-0 flex-1">
          <div className="truncate text-[16px] font-semibold text-ink-900">{user?.displayName}</div>
          <div className="truncate text-[12.5px] text-ink-400">{user?.email}</div>
        </div>
        <span className="inline-flex items-center gap-1.5 rounded-full bg-brand-50 px-2.5 py-0.5 text-[11.5px] font-semibold text-brand-800 ring-1 ring-inset ring-brand-200">
          <span className="h-1.5 w-1.5 rounded-full bg-brand-500" />
          {roleLabel}
        </span>
      </div>

      {/* Personal Information */}
      <Section title={t('profile.personalInfo')} subtitle={t('profile.personalInfoSubtitle')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('profile.labelEmail')} helper={t('profile.emailHelper')} htmlFor="profile-email">
            <input
              id="profile-email"
              value={user?.email ?? ''}
              disabled
              className="w-full rounded-lg border border-line-strong bg-surface-sunken px-3.5 py-2.5 text-[14px] text-ink-400"
            />
          </Field>
          <Field label={t('profile.labelDisplayName')} htmlFor="profile-display-name">
            <input
              id="profile-display-name"
              value={displayName}
              onChange={(e) => setDisplayName(e.target.value)}
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
            />
          </Field>
          <Field label={t('profile.labelCarPlate')} htmlFor="profile-car-license">
            <input
              id="profile-car-license"
              value={carLicensePlate}
              onChange={(e) => setCarLicensePlate(e.target.value)}
              placeholder="GP-AB 1234"
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900 placeholder:text-ink-300 num"
            />
          </Field>
          <div className="hidden sm:block" />
          <Field
            label={t('profile.labelPreferredLocation')}
            helper={t('profile.preferredLocationHelper')}
            htmlFor="preferredLocation"
          >
            <select
              id="preferredLocation"
              value={preferredLocationId ?? ''}
              onChange={(e) => {
                const next = e.target.value || null;
                setPreferredLocationId(next);
                setPreferredSlotId(null);
              }}
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
            >
              <option value="">{t('common.noPreference')}</option>
              {locations.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </select>
          </Field>
          <Field
            label={t('profile.labelPreferredSlot')}
            helper={t('profile.preferredSlotHelper')}
            htmlFor="preferredSlot"
          >
            <select
              id="preferredSlot"
              value={preferredSlotId ?? ''}
              onChange={(e) => setPreferredSlotId(e.target.value || null)}
              disabled={!preferredLocationId || slots.length === 0}
              className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900 disabled:bg-surface-sunken disabled:text-ink-400"
            >
              <option value="">{t('common.noPreference')}</option>
              {slots.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.label ? `${s.slotNumber} — ${s.label}` : s.slotNumber}
                </option>
              ))}
            </select>
          </Field>
        </div>
        <div className="mt-5 flex justify-end">
          <button
            type="button"
            onClick={handleSave}
            disabled={saving || !displayName}
            className="min-h-11 w-full rounded-lg bg-brand-500 px-5 py-2.5 text-[13.5px] font-medium text-white shadow-sm transition-colors hover:bg-brand-600 disabled:opacity-60 sm:min-h-0 sm:w-auto"
          >
            {saving ? t('common.saving') : t('common.save')}
          </button>
        </div>
      </Section>

      {/* Change password — local accounts only (SSO accounts have no password) */}
      {user?.hasPassword && (
        <Section title={t('profile.changePassword.title')} subtitle={t('profile.changePassword.subtitle')}>
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void handleChangePassword();
            }}
          >
            {passwordError && (
              <div role="alert" className="mb-4 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
                {passwordError}
              </div>
            )}
            {passwordChanged && (
              <div role="status" className="mb-4 flex items-start gap-2.5 rounded-lg border border-emerald-200 bg-emerald-50 px-3.5 py-3 text-[13px] text-emerald-800">
                <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
                </svg>
                {t('profile.changePassword.success')}
              </div>
            )}
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('profile.changePassword.current')} htmlFor="profile-current-password">
                <PasswordInput
                  id="profile-current-password"
                  value={currentPassword}
                  onChange={(e) => setCurrentPassword(e.target.value)}
                  required
                  autoComplete="current-password"
                  className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
                />
              </Field>
              <div className="hidden sm:block" />
              <Field label={t('profile.changePassword.new')} helper={t('auth.passwordHint')} htmlFor="profile-new-password">
                <PasswordInput
                  id="profile-new-password"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  required
                  minLength={8}
                  autoComplete="new-password"
                  className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
                />
              </Field>
              <Field label={t('profile.changePassword.confirm')} htmlFor="profile-confirm-password">
                <PasswordInput
                  id="profile-confirm-password"
                  value={confirmNewPassword}
                  onChange={(e) => setConfirmNewPassword(e.target.value)}
                  required
                  autoComplete="new-password"
                  className="w-full rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[14px] text-ink-900"
                />
              </Field>
            </div>
            <div className="mt-5 flex justify-end">
              <button
                type="submit"
                disabled={changingPassword || !currentPassword || !newPassword || !confirmNewPassword}
                className="min-h-11 w-full rounded-lg bg-brand-500 px-5 py-2.5 text-[13.5px] font-medium text-white shadow-sm transition-colors hover:bg-brand-600 disabled:opacity-60 sm:min-h-0 sm:w-auto"
              >
                {changingPassword ? t('profile.changePassword.saving') : t('profile.changePassword.submit')}
              </button>
            </div>
          </form>
        </Section>
      )}

      {/* Push */}
      {pushSupported && (
        <Section title={t('profile.push')} subtitle={t('profile.pushSubtitle')}>
          {pushPermission === 'denied' ? (
            <div className="flex items-start gap-2.5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">
              <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M18.364 18.364A9 9 0 005.636 5.636m12.728 12.728A9 9 0 015.636 5.636m12.728 12.728L5.636 5.636" />
              </svg>
              {t('profile.pushDenied')}
            </div>
          ) : pushSubscribed ? (
            <div className="flex flex-col gap-2.5">
              <div className="flex flex-wrap items-center gap-3 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-3">
                <span className="inline-flex items-center gap-2 text-[13px] font-medium text-emerald-800">
                  <span className="h-2 w-2 rounded-full bg-emerald-500" />
                  {t('profile.pushEnabled')}
                </span>
                <div className="flex w-full items-center gap-2 sm:ml-auto sm:w-auto">
                  <button
                    type="button"
                    onClick={() => void handleTestPush()}
                    disabled={pushTestState.kind === 'sending'}
                    className="min-h-10 flex-1 rounded-lg border border-emerald-300 bg-white px-3 py-1.5 text-[12px] font-medium text-emerald-700 hover:bg-emerald-50 disabled:opacity-60 sm:min-h-0 sm:flex-none"
                  >
                    {pushTestState.kind === 'sending' ? t('profile.pushTestSending') : t('profile.pushTest')}
                  </button>
                  <button
                    type="button"
                    onClick={() => void handleDisablePush()}
                    className="min-h-10 flex-1 rounded-lg border border-emerald-300 bg-white px-3 py-1.5 text-[12px] font-medium text-emerald-700 hover:bg-emerald-50 sm:min-h-0 sm:flex-none"
                  >
                    {t('profile.pushDisable')}
                  </button>
                </div>
              </div>
              {pushTestState.kind === 'ok' && (
                <p role="status" className="text-[13px] text-emerald-800">
                  {t('profile.pushTestOk', { count: pushTestState.delivered })}
                </p>
              )}
              {pushTestState.kind === 'error' && (
                <p role="alert" className="text-[13px] text-rose-700">{pushTestState.message}</p>
              )}
            </div>
          ) : (
            <div className="flex flex-col gap-2">
              <button
                type="button"
                onClick={handleEnablePush}
                className="rounded-lg bg-brand-500 px-4 py-2.5 text-[13.5px] font-medium text-white shadow-sm hover:bg-brand-600"
              >
                {t('profile.pushEnable')}
              </button>
              {pushError && (
                <p className="text-[13px] text-rose-700">{pushError}</p>
              )}
            </div>
          )}
        </Section>
      )}

      {/* DSGVO Export */}
      <Section
        title={t('profile.dsgvoTitle')}
        subtitle={t('profile.dsgvoSubtitle')}
      >
        <button
          type="button"
          onClick={handleExport}
          className="rounded-lg border border-line-strong bg-white px-4 py-2.5 text-[13.5px] font-medium text-ink-700 hover:bg-surface-sunken"
        >
          {t('profile.downloadData')}
        </button>
      </Section>

      {/* Danger zone */}
      <div className="mt-6 rounded-card border border-rose-200 bg-rose-50/50 p-4 sm:p-6">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-start">
          <div className="grid h-10 w-10 shrink-0 place-items-center rounded-lg bg-rose-100 text-rose-600">
            <svg className="h-5 w-5" fill="none" stroke="currentColor" viewBox="0 0 24 24">
              <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z" />
            </svg>
          </div>
          <div className="min-w-0 flex-1">
            <h2 className="text-[15px] font-semibold text-rose-900">{t('profile.dangerTitle')}</h2>
            <p className="mt-1 max-w-lg text-[12.5px] leading-relaxed text-rose-700">
              {t('profile.dangerDescription')}
            </p>
          </div>
          <button
            type="button"
            onClick={() => setShowDeleteDialog(true)}
            className="min-h-11 w-full rounded-lg bg-rose-600 px-4 py-2.5 text-[13.5px] font-medium text-white shadow-sm hover:bg-rose-700 sm:min-h-0 sm:w-auto"
          >
            {t('profile.deleteBtn')}
          </button>
        </div>
      </div>

      <ConfirmDialog
        isOpen={showDeleteDialog}
        title={t('profile.deleteDialogTitle')}
        message={t('profile.deleteDialogMessage')}
        confirmLabel={t('profile.deleteDialogConfirm')}
        cancelLabel={t('profile.deleteDialogKeep')}
        onConfirm={handleDelete}
        onCancel={() => setShowDeleteDialog(false)}
        isLoading={deleting}
      />
    </div>
  );
}

function Section({
  title,
  subtitle,
  children,
}: {
  title: string;
  subtitle?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="mt-6 rounded-card border border-line bg-white p-6 shadow-card">
      <div>
        <h2 className="text-[15px] font-semibold text-ink-900">{title}</h2>
        {subtitle && <p className="mt-0.5 text-[12.5px] text-ink-400">{subtitle}</p>}
      </div>
      <div className="mt-5">{children}</div>
    </div>
  );
}

function Field({
  label,
  helper,
  htmlFor,
  children,
}: {
  label: string;
  helper?: string;
  htmlFor?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1.5 block text-[12.5px] font-medium text-ink-500">{label}</label>
      {children}
      {helper && <p className="mt-1 text-[11.5px] leading-relaxed text-ink-400">{helper}</p>}
    </div>
  );
}
