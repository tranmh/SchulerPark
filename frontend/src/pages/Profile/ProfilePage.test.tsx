import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ProfilePage } from './ProfilePage';
import { renderWithRouter, createMockAuth, mockUser } from '../../test/helpers';
import type { MockAuthValue } from '../../test/helpers';

let mockAuth: MockAuthValue & { isSuperAdmin: boolean };

vi.mock('../../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

vi.mock('../../services/profileService', () => ({
  profileService: {
    updateProfile: vi.fn(),
    getProfile: vi.fn(),
    changePassword: vi.fn(),
    exportData: vi.fn(),
    requestDeletion: vi.fn(),
  },
}));

vi.mock('../../services/locationService', () => ({
  locationService: {
    getLocations: vi.fn().mockResolvedValue([]),
    getSlots: vi.fn().mockResolvedValue([]),
  },
}));

import { profileService } from '../../services/profileService';

const baseUser = { ...mockUser, preferredLocationId: null, preferredSlotId: null };

function authWith(user: typeof baseUser) {
  return { ...createMockAuth({ isAuthenticated: true, user }), isSuperAdmin: false };
}

function nameInput() {
  return screen.getByLabelText(/display name|anzeigename/i) as HTMLInputElement;
}

function plateInput() {
  return screen.getByLabelText(/licen[cs]e plate|kennzeichen/i) as HTMLInputElement;
}

/** The form starts from the server's copy of the profile and pushes saves back into the auth context. */
describe('ProfilePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockAuth = authWith(baseUser);
  });

  it('re-reads the profile from the server on mount', async () => {
    renderWithRouter(<ProfilePage />);

    await waitFor(() => expect(mockAuth.refreshUser).toHaveBeenCalledTimes(1));
    expect(nameInput().value).toBe('Test User');
    expect(plateInput().value).toBe('GP-TE 1234');
  });

  it('follows a context user update while the form is pristine', async () => {
    const { rerender } = renderWithRouter(<ProfilePage />);

    mockAuth = authWith({ ...baseUser, displayName: 'Fresh Name', carLicensePlate: 'GP-FR 9' });
    rerender(<ProfilePage />);

    await waitFor(() => expect(nameInput().value).toBe('Fresh Name'));
    expect(plateInput().value).toBe('GP-FR 9');
    expect(screen.getByText('Fresh Name')).toBeInTheDocument();
  });

  it('does not clobber in-progress edits when a background refresh arrives', async () => {
    const user = userEvent.setup();
    const { rerender } = renderWithRouter(<ProfilePage />);

    await user.clear(nameInput());
    await user.type(nameInput(), 'Typing Here');

    mockAuth = authWith({ ...baseUser, displayName: 'Fresh Name', carLicensePlate: 'GP-FR 9' });
    rerender(<ProfilePage />);

    expect(nameInput().value).toBe('Typing Here');
    // The untouched field is left alone too — the form is treated as one draft.
    expect(plateInput().value).toBe('GP-TE 1234');
  });

  it('pushes the saved profile into the auth context and shows the server copy', async () => {
    const user = userEvent.setup();
    const saved = { ...baseUser, displayName: 'Saved Name', carLicensePlate: 'GP-SA 1' };
    vi.mocked(profileService.updateProfile).mockResolvedValue(saved);
    renderWithRouter(<ProfilePage />);

    await user.clear(nameInput());
    await user.type(nameInput(), 'Saved Name');
    await user.click(screen.getByRole('button', { name: /save changes|änderungen speichern/i }));

    await waitFor(() => expect(mockAuth.updateUser).toHaveBeenCalledWith(saved));
    expect(profileService.updateProfile).toHaveBeenCalledWith({
      displayName: 'Saved Name',
      carLicensePlate: 'GP-TE 1234',
      preferredLocationId: null,
      preferredSlotId: null,
    });

    // After the save the form is pristine again and follows the (now updated) context user.
    mockAuth = authWith(saved);
    renderWithRouter(<ProfilePage />);
    await waitFor(() => expect(screen.getAllByDisplayValue('GP-SA 1').length).toBeGreaterThan(0));
  });

  it('keeps the edits and shows an error when saving fails', async () => {
    const user = userEvent.setup();
    vi.mocked(profileService.updateProfile).mockRejectedValue(new Error('500'));
    renderWithRouter(<ProfilePage />);

    await user.clear(nameInput());
    await user.type(nameInput(), 'Unsaved');
    await user.click(screen.getByRole('button', { name: /save changes|änderungen speichern/i }));

    await waitFor(() => expect(screen.getByText(/could not|konnte nicht|fehlgeschlagen|failed/i)).toBeInTheDocument());
    expect(mockAuth.updateUser).not.toHaveBeenCalled();
    expect(nameInput().value).toBe('Unsaved');
  });
});
