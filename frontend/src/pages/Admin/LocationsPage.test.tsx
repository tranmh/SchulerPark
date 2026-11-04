import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { LocationsPage } from './LocationsPage';
import type { AdminLocation } from '../../types/admin';

vi.mock('../../utils/berlinTime', () => ({ todayInBerlin: () => '2026-11-03' }));

vi.mock('../../services/adminService', () => ({
  adminService: {
    getLocations: vi.fn(),
    createLocation: vi.fn(),
    updateLocation: vi.fn(),
    setAlgorithm: vi.fn(),
    countBookings: vi.fn(),
    deactivateLocation: vi.fn(),
  },
}));

import { adminService } from '../../services/adminService';

const loc = (id: string, name: string, extra: Partial<AdminLocation> = {}): AdminLocation => ({
  id, name, isActive: true, defaultAlgorithm: 'PureRandom', totalSlots: 5, activeSlots: 3, ...extra,
});

const nord = loc('l1', 'Nord Halle');
const old = loc('l2', 'Old Yard', { isActive: false, defaultAlgorithm: 'RoundRobin', totalSlots: 2, activeSlots: 0 });

const rowOf = (name: string) => within(screen.getByText(name).closest('tr') as HTMLElement);
const dialog = () => within(screen.getByRole('alertdialog'));
const modal = () => within(screen.getByRole('dialog'));

// B51
describe('LocationsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(adminService.getLocations).mockResolvedValue([nord, old]);
    vi.mocked(adminService.countBookings).mockResolvedValue(0);
  });

  it('lists locations with status, slot counts, algorithm and actions', async () => {
    render(<LocationsPage />);

    expect(await screen.findByRole('heading', { name: 'Locations' })).toBeInTheDocument();
    const nordRow = rowOf('Nord Halle');
    expect(nordRow.getByText('NH')).toBeInTheDocument();
    expect(nordRow.getByText('Active')).toBeInTheDocument();
    expect(nordRow.getByText('3')).toBeInTheDocument();
    expect(nordRow.getByText('/ 5')).toBeInTheDocument();
    expect(nordRow.getByRole('combobox')).toHaveValue('PureRandom');
    expect(nordRow.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
    expect(nordRow.getByRole('button', { name: 'Deactivate' })).toBeInTheDocument();

    const oldRow = rowOf('Old Yard');
    expect(oldRow.getByText('Inactive')).toBeInTheDocument();
    expect(oldRow.getByRole('combobox')).toHaveValue('RoundRobin');
    expect(oldRow.queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument();
  });

  it('shows the empty state when there are no locations', async () => {
    vi.mocked(adminService.getLocations).mockResolvedValue([]);
    render(<LocationsPage />);

    expect(await screen.findByText('No locations yet. Create your first one above.')).toBeInTheDocument();
  });

  it('shows an error when the list cannot be loaded', async () => {
    vi.mocked(adminService.getLocations).mockRejectedValue(new Error('500'));
    render(<LocationsPage />);

    expect(await screen.findByText('Failed to load locations.')).toBeInTheDocument();
  });

  it('creates a location with the typed name and reloads the list', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.createLocation).mockResolvedValue(loc('l3', 'West'));
    render(<LocationsPage />);

    await user.click(await screen.findByRole('button', { name: 'New location' }));
    expect(modal().getByText('New location')).toBeInTheDocument();
    // The create form has no "Active" toggle; that only exists when editing.
    expect(modal().queryByRole('checkbox')).not.toBeInTheDocument();
    expect(modal().getByRole('button', { name: 'Save' })).toBeDisabled();

    await user.type(screen.getByLabelText('Name'), 'West');
    expect(modal().getByRole('button', { name: 'Save' })).toBeEnabled();

    vi.mocked(adminService.getLocations).mockResolvedValue([nord, old, loc('l3', 'West')]);
    await user.click(modal().getByRole('button', { name: 'Save' }));

    expect(adminService.createLocation).toHaveBeenCalledWith({ name: 'West' });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(await screen.findByText('West')).toBeInTheDocument();
    expect(adminService.getLocations).toHaveBeenCalledTimes(2);
  });

  it('keeps Save disabled when the name is cleared again', async () => {
    const user = userEvent.setup();
    render(<LocationsPage />);

    await user.click(await screen.findByRole('button', { name: 'New location' }));
    await user.type(screen.getByLabelText('Name'), 'X');
    await user.clear(screen.getByLabelText('Name'));

    expect(modal().getByRole('button', { name: 'Save' })).toBeDisabled();
    expect(adminService.createLocation).not.toHaveBeenCalled();
  });

  it('cancel closes the form without saving', async () => {
    const user = userEvent.setup();
    render(<LocationsPage />);

    await user.click(await screen.findByRole('button', { name: 'New location' }));
    await user.type(screen.getByLabelText('Name'), 'Draft');
    await user.click(modal().getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(adminService.createLocation).not.toHaveBeenCalled();
  });

  it('shows an error and keeps the form open when creating fails (e.g. duplicate name)', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.createLocation).mockRejectedValue({ response: { status: 409, data: { error: 'Duplicate' } } });
    render(<LocationsPage />);

    await user.click(await screen.findByRole('button', { name: 'New location' }));
    await user.type(screen.getByLabelText('Name'), 'Nord Halle');
    await user.click(modal().getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Failed to save location.')).toBeInTheDocument();
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(modal().getByRole('button', { name: 'Save' })).toBeEnabled();
  });

  it('edits a location: prefilled name and active flag are sent to updateLocation', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.updateLocation).mockResolvedValue({ ...nord, name: 'Nord Halle 2', isActive: false });
    render(<LocationsPage />);

    await screen.findByText('Nord Halle');
    await user.click(rowOf('Nord Halle').getByRole('button', { name: 'Edit' }));

    expect(modal().getByText('Edit location')).toBeInTheDocument();
    const name = screen.getByLabelText('Name');
    expect(name).toHaveValue('Nord Halle');
    const active = screen.getByLabelText('Active');
    expect(active).toBeChecked();

    await user.type(name, ' 2');
    await user.click(active);
    await user.click(modal().getByRole('button', { name: 'Save' }));

    expect(adminService.updateLocation).toHaveBeenCalledWith('l1', { name: 'Nord Halle 2', isActive: false });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    expect(adminService.createLocation).not.toHaveBeenCalled();
    expect(adminService.getLocations).toHaveBeenCalledTimes(2);
  });

  it('shows an error when updating fails', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.updateLocation).mockRejectedValue(new Error('500'));
    render(<LocationsPage />);

    await screen.findByText('Old Yard');
    await user.click(rowOf('Old Yard').getByRole('button', { name: 'Edit' }));
    expect(screen.getByLabelText('Active')).not.toBeChecked();
    await user.click(modal().getByRole('button', { name: 'Save' }));

    expect(await screen.findByText('Failed to save location.')).toBeInTheDocument();
  });

  it('changes the lottery algorithm and reloads', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.setAlgorithm).mockResolvedValue(undefined as never);
    render(<LocationsPage />);

    await screen.findByText('Nord Halle');
    vi.mocked(adminService.getLocations).mockResolvedValue([{ ...nord, defaultAlgorithm: 'WeightedHistory' }, old]);
    await user.selectOptions(rowOf('Nord Halle').getByRole('combobox'), 'WeightedHistory');

    expect(adminService.setAlgorithm).toHaveBeenCalledWith('l1', 'WeightedHistory');
    await waitFor(() => expect(adminService.getLocations).toHaveBeenCalledTimes(2));
    expect(rowOf('Nord Halle').getByRole('combobox')).toHaveValue('WeightedHistory');
  });

  it('shows an error when the algorithm change fails', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.setAlgorithm).mockRejectedValue(new Error('500'));
    render(<LocationsPage />);

    await screen.findByText('Nord Halle');
    await user.selectOptions(rowOf('Nord Halle').getByRole('combobox'), 'RoundRobin');

    expect(await screen.findByText('Failed to update algorithm.')).toBeInTheDocument();
    expect(adminService.getLocations).toHaveBeenCalledTimes(1);
  });

  describe('deactivation', () => {
    it('previews the affected bookings and deactivates after confirmation', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.countBookings).mockResolvedValue(2);
      vi.mocked(adminService.deactivateLocation).mockResolvedValue({ affected: 2, reassigned: 0, waitlisted: 0, cancelled: 2 });
      render(<LocationsPage />);

      await screen.findByText('Nord Halle');
      await user.click(rowOf('Nord Halle').getByRole('button', { name: 'Deactivate' }));

      expect(adminService.countBookings).toHaveBeenCalledWith({
        locationId: 'l1', from: '2026-11-03', status: 'Pending,Won,Confirmed,Waitlisted',
      });
      expect(dialog().getByText('Deactivate location')).toBeInTheDocument();
      expect(await dialog().findByText(
        'Location Nord Halle will be deactivated. No further bookings can be made. 2 bookings will be cancelled; the users will be notified.'
      )).toBeInTheDocument();

      vi.mocked(adminService.getLocations).mockResolvedValue([{ ...nord, isActive: false }, old]);
      await user.click(dialog().getByRole('button', { name: 'Deactivate' }));

      expect(adminService.deactivateLocation).toHaveBeenCalledWith('l1');
      await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
      expect(await screen.findByRole('status')).toHaveTextContent('Affected: 2 · moved: 0 · waitlisted: 0 · cancelled: 2');
      expect(rowOf('Nord Halle').getByText('Inactive')).toBeInTheDocument();
      expect(rowOf('Nord Halle').queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument();
    });

    it('uses the singular wording for one affected booking', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.countBookings).mockResolvedValue(1);
      render(<LocationsPage />);

      await screen.findByText('Nord Halle');
      await user.click(rowOf('Nord Halle').getByRole('button', { name: 'Deactivate' }));

      expect(await dialog().findByText(/1 booking will be cancelled; the users will be notified\./)).toBeInTheDocument();
    });

    it('reports no affected bookings when the count is zero or the preview fails', async () => {
      const user = userEvent.setup();
      render(<LocationsPage />);

      await screen.findByText('Nord Halle');
      await user.click(rowOf('Nord Halle').getByRole('button', { name: 'Deactivate' }));
      expect(await dialog().findByText(/No existing bookings are affected\./)).toBeInTheDocument();
      await user.click(dialog().getByRole('button', { name: 'Cancel' }));
      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

      vi.mocked(adminService.countBookings).mockRejectedValue(new Error('500'));
      await user.click(rowOf('Nord Halle').getByRole('button', { name: 'Deactivate' }));
      expect(await dialog().findByText(/No existing bookings are affected\./)).toBeInTheDocument();
    });

    it('cancelling the dialog does not deactivate', async () => {
      const user = userEvent.setup();
      render(<LocationsPage />);

      await screen.findByText('Nord Halle');
      await user.click(rowOf('Nord Halle').getByRole('button', { name: 'Deactivate' }));
      await user.click(dialog().getByRole('button', { name: 'Cancel' }));

      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
      expect(adminService.deactivateLocation).not.toHaveBeenCalled();
      expect(rowOf('Nord Halle').getByText('Active')).toBeInTheDocument();
    });

    it('shows an error and keeps the dialog open when deactivation fails', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.deactivateLocation).mockRejectedValue(new Error('500'));
      render(<LocationsPage />);

      await screen.findByText('Nord Halle');
      await user.click(rowOf('Nord Halle').getByRole('button', { name: 'Deactivate' }));
      await user.click(dialog().getByRole('button', { name: 'Deactivate' }));

      expect(await screen.findByText('Failed to deactivate location.')).toBeInTheDocument();
      expect(screen.getByRole('alertdialog')).toBeInTheDocument();
      expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });
  });
});
