import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { SlotsPage } from './SlotsPage';
import type { AdminLocation, AdminSlot } from '../../types/admin';

vi.mock('../../utils/berlinTime', () => ({ todayInBerlin: () => '2026-11-03' }));

vi.mock('../../services/adminService', () => ({
  adminService: {
    getLocations: vi.fn(),
    getSlots: vi.fn(),
    createSlot: vi.fn(),
    updateSlot: vi.fn(),
    countBookings: vi.fn(),
    deactivateSlot: vi.fn(),
  },
}));

import { adminService } from '../../services/adminService';

const loc = (id: string, name: string, isActive = true): AdminLocation => ({
  id, name, isActive, defaultAlgorithm: 'PureRandom', totalSlots: 2, activeSlots: 2,
});

const slot = (id: string, slotNumber: string, extra: Partial<AdminSlot> = {}): AdminSlot => ({
  id, locationId: 'l1', slotNumber, label: null, isActive: true, ...extra,
});

const p1 = slot('s1', 'P001', { label: 'Near entrance' });
const p2 = slot('s2', 'P002', { isActive: false });

const rowOf = (slotNumber: string) => within(screen.getByText(slotNumber).closest('tr') as HTMLElement);
const dialog = () => within(screen.getByRole('alertdialog'));
const modal = () => within(screen.getByRole('dialog'));

describe('SlotsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(adminService.getLocations).mockResolvedValue([loc('l0', 'Old', false), loc('l1', 'Nord'), loc('l2', 'Süd')]);
    vi.mocked(adminService.getSlots).mockResolvedValue([p1, p2]);
    vi.mocked(adminService.countBookings).mockResolvedValue(0);
  });

  it('offers only active locations, selects the first and loads its slots', async () => {
    render(<SlotsPage />);

    const select = await screen.findByLabelText('Location');
    expect(within(select).queryByRole('option', { name: 'Old' })).not.toBeInTheDocument();
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['Nord', 'Süd']);
    expect(select).toHaveValue('l1');
    await waitFor(() => expect(adminService.getSlots).toHaveBeenCalledWith('l1'));

    const active = rowOf('P001');
    expect(active.getByText('Near entrance')).toBeInTheDocument();
    expect(active.getByText('Active')).toBeInTheDocument();
    expect(active.getByRole('button', { name: 'Deactivate' })).toBeInTheDocument();

    const inactive = rowOf('P002');
    expect(inactive.getByText('—')).toBeInTheDocument();
    expect(inactive.getByText('Inactive')).toBeInTheDocument();
    expect(inactive.getByRole('button', { name: 'Edit' })).toBeInTheDocument();
    expect(inactive.queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument();
  });

  it('shows the empty state when the location has no slots', async () => {
    vi.mocked(adminService.getSlots).mockResolvedValue([]);
    render(<SlotsPage />);

    expect(await screen.findByText('No slots configured for this location.')).toBeInTheDocument();
  });

  it('disables "New slot" and loads nothing when there is no active location', async () => {
    vi.mocked(adminService.getLocations).mockResolvedValue([loc('l0', 'Old', false)]);
    render(<SlotsPage />);

    expect(await screen.findByRole('button', { name: 'New slot' })).toBeDisabled();
    expect(adminService.getSlots).not.toHaveBeenCalled();
  });

  it('reloads the slots when another location is chosen', async () => {
    const user = userEvent.setup();
    render(<SlotsPage />);

    await user.selectOptions(await screen.findByLabelText('Location'), 'l2');

    await waitFor(() => expect(adminService.getSlots).toHaveBeenCalledWith('l2'));
  });

  it('shows errors when locations or slots cannot be loaded', async () => {
    vi.mocked(adminService.getLocations).mockRejectedValue(new Error('500'));
    const { unmount } = render(<SlotsPage />);
    expect(await screen.findByText('Failed to load locations.')).toBeInTheDocument();
    unmount();

    vi.mocked(adminService.getLocations).mockResolvedValue([loc('l1', 'Nord')]);
    vi.mocked(adminService.getSlots).mockRejectedValue(new Error('500'));
    render(<SlotsPage />);
    expect(await screen.findByText('Failed to load slots.')).toBeInTheDocument();
  });

  describe('create', () => {
    it('creates a slot for the selected location without a label when left empty', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.createSlot).mockResolvedValue(slot('s3', 'P003'));
      render(<SlotsPage />);

      await user.click(await screen.findByRole('button', { name: 'New slot' }));
      expect(modal().getByText('New slot')).toBeInTheDocument();
      expect(modal().queryByRole('checkbox')).not.toBeInTheDocument();
      expect(modal().getByRole('button', { name: 'Save' })).toBeDisabled();

      await user.type(screen.getByLabelText('Slot number'), 'P003');
      expect(modal().getByRole('button', { name: 'Save' })).toBeEnabled();

      vi.mocked(adminService.getSlots).mockResolvedValue([p1, p2, slot('s3', 'P003')]);
      await user.click(modal().getByRole('button', { name: 'Save' }));

      expect(adminService.createSlot).toHaveBeenCalledWith({ locationId: 'l1', slotNumber: 'P003', label: undefined });
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(await screen.findByText('P003')).toBeInTheDocument();
      expect(adminService.getSlots).toHaveBeenCalledTimes(2);
    });

    it('sends the label when one is typed and targets the currently selected location', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.createSlot).mockResolvedValue(slot('s3', 'P003'));
      render(<SlotsPage />);

      await user.selectOptions(await screen.findByLabelText('Location'), 'l2');
      await user.click(screen.getByRole('button', { name: 'New slot' }));
      await user.type(screen.getByLabelText('Slot number'), 'P003');
      await user.type(screen.getByLabelText('Label (optional)'), 'Roof');
      await user.click(modal().getByRole('button', { name: 'Save' }));

      expect(adminService.createSlot).toHaveBeenCalledWith({ locationId: 'l2', slotNumber: 'P003', label: 'Roof' });
      await waitFor(() => expect(adminService.getSlots).toHaveBeenLastCalledWith('l2'));
    });

    it('cancel closes the form without saving', async () => {
      const user = userEvent.setup();
      render(<SlotsPage />);

      await user.click(await screen.findByRole('button', { name: 'New slot' }));
      await user.type(screen.getByLabelText('Slot number'), 'P009');
      await user.click(modal().getByRole('button', { name: 'Cancel' }));

      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(adminService.createSlot).not.toHaveBeenCalled();
    });

    it('shows an error and keeps the form open when creating fails', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.createSlot).mockRejectedValue({ response: { status: 409 } });
      render(<SlotsPage />);

      await user.click(await screen.findByRole('button', { name: 'New slot' }));
      await user.type(screen.getByLabelText('Slot number'), 'P001');
      await user.click(modal().getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('Failed to save slot.')).toBeInTheDocument();
      expect(screen.getByRole('dialog')).toBeInTheDocument();
      expect(adminService.getSlots).toHaveBeenCalledTimes(1);
    });
  });

  describe('edit', () => {
    it('prefills the form and sends number, label and active flag to updateSlot', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.updateSlot).mockResolvedValue({ ...p1, slotNumber: 'P001A', isActive: false });
      render(<SlotsPage />);

      await screen.findByText('P001');
      await user.click(rowOf('P001').getByRole('button', { name: 'Edit' }));

      expect(modal().getByText('Edit slot')).toBeInTheDocument();
      expect(screen.getByLabelText('Slot number')).toHaveValue('P001');
      expect(screen.getByLabelText('Label (optional)')).toHaveValue('Near entrance');
      expect(screen.getByLabelText('Active')).toBeChecked();

      await user.type(screen.getByLabelText('Slot number'), 'A');
      await user.click(screen.getByLabelText('Active'));
      await user.click(modal().getByRole('button', { name: 'Save' }));

      expect(adminService.updateSlot).toHaveBeenCalledWith('s1', { slotNumber: 'P001A', label: 'Near entrance', isActive: false });
      expect(adminService.createSlot).not.toHaveBeenCalled();
      await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
      expect(adminService.getSlots).toHaveBeenCalledTimes(2);
    });

    it('re-activates an inactive slot and drops a cleared label', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.updateSlot).mockResolvedValue({ ...p2, isActive: true });
      render(<SlotsPage />);

      await screen.findByText('P002');
      await user.click(rowOf('P002').getByRole('button', { name: 'Edit' }));
      expect(screen.getByLabelText('Label (optional)')).toHaveValue('');
      expect(screen.getByLabelText('Active')).not.toBeChecked();

      await user.click(screen.getByLabelText('Active'));
      await user.click(modal().getByRole('button', { name: 'Save' }));

      expect(adminService.updateSlot).toHaveBeenCalledWith('s2', { slotNumber: 'P002', label: undefined, isActive: true });
    });

    it('clearing the slot number disables Save', async () => {
      const user = userEvent.setup();
      render(<SlotsPage />);

      await screen.findByText('P001');
      await user.click(rowOf('P001').getByRole('button', { name: 'Edit' }));
      await user.clear(screen.getByLabelText('Slot number'));

      expect(modal().getByRole('button', { name: 'Save' })).toBeDisabled();
    });

    it('shows an error when updating fails', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.updateSlot).mockRejectedValue(new Error('500'));
      render(<SlotsPage />);

      await screen.findByText('P001');
      await user.click(rowOf('P001').getByRole('button', { name: 'Edit' }));
      await user.click(modal().getByRole('button', { name: 'Save' }));

      expect(await screen.findByText('Failed to save slot.')).toBeInTheDocument();
    });
  });

  describe('deactivate', () => {
    it('previews the affected bookings and deactivates after confirmation', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.countBookings).mockResolvedValue(3);
      vi.mocked(adminService.deactivateSlot).mockResolvedValue({ affected: 3, reassigned: 2, waitlisted: 1, cancelled: 0 });
      render(<SlotsPage />);

      await screen.findByText('P001');
      await user.click(rowOf('P001').getByRole('button', { name: 'Deactivate' }));

      expect(adminService.countBookings).toHaveBeenCalledWith({ parkingSlotId: 's1', from: '2026-11-03', status: 'Won,Confirmed' });
      expect(dialog().getByText('Deactivate slot')).toBeInTheDocument();
      expect(await dialog().findByText(
        'Slot P001 will be deactivated and is no longer available from now on. 3 won or confirmed bookings will be moved to other slots or waitlisted.'
      )).toBeInTheDocument();

      vi.mocked(adminService.getSlots).mockResolvedValue([{ ...p1, isActive: false }, p2]);
      await user.click(dialog().getByRole('button', { name: 'Deactivate' }));

      expect(adminService.deactivateSlot).toHaveBeenCalledWith('s1');
      await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
      expect(await screen.findByRole('status')).toHaveTextContent('Affected: 3 · moved: 2 · waitlisted: 1 · cancelled: 0');
      expect(rowOf('P001').getByText('Inactive')).toBeInTheDocument();
      expect(adminService.getSlots).toHaveBeenCalledTimes(2);
    });

    it('uses the singular wording for one affected booking', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.countBookings).mockResolvedValue(1);
      render(<SlotsPage />);

      await screen.findByText('P001');
      await user.click(rowOf('P001').getByRole('button', { name: 'Deactivate' }));

      expect(await dialog().findByText(/1 won or confirmed booking will be moved to another slot or waitlisted\./)).toBeInTheDocument();
    });

    it('reports no affected bookings when the preview fails', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.countBookings).mockRejectedValue(new Error('500'));
      render(<SlotsPage />);

      await screen.findByText('P001');
      await user.click(rowOf('P001').getByRole('button', { name: 'Deactivate' }));

      expect(await dialog().findByText(/No existing bookings are affected\./)).toBeInTheDocument();
    });

    it('cancelling the dialog does not deactivate', async () => {
      const user = userEvent.setup();
      render(<SlotsPage />);

      await screen.findByText('P001');
      await user.click(rowOf('P001').getByRole('button', { name: 'Deactivate' }));
      await user.click(dialog().getByRole('button', { name: 'Cancel' }));

      expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
      expect(adminService.deactivateSlot).not.toHaveBeenCalled();
    });

    it('shows an error and keeps the dialog open when deactivation fails', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.deactivateSlot).mockRejectedValue(new Error('500'));
      render(<SlotsPage />);

      await screen.findByText('P001');
      await user.click(rowOf('P001').getByRole('button', { name: 'Deactivate' }));
      await user.click(dialog().getByRole('button', { name: 'Deactivate' }));

      expect(await screen.findByText('Failed to deactivate slot.')).toBeInTheDocument();
      expect(screen.getByRole('alertdialog')).toBeInTheDocument();
      expect(screen.queryByRole('status')).not.toBeInTheDocument();
    });
  });
});
