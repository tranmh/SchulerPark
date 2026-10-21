import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, waitFor } from '@testing-library/react';
import { renderWithRouter } from '../../test/helpers';
import type { AdminLocation } from '../../types/admin';
import { SlotsPage } from './SlotsPage';
import { GridLayoutPage } from './GridLayoutPage';
import { BlockedDaysPage } from './BlockedDaysPage';

vi.mock('../../services/adminService', () => ({
  adminService: {
    getLocations: vi.fn(),
    getSlots: vi.fn(),
    getBlockedDays: vi.fn(),
    getGridConfiguration: vi.fn(),
  },
}));

import { adminService } from '../../services/adminService';

const locations: AdminLocation[] = [
  { id: 'old', name: 'Old Yard (closed)', isActive: false, defaultAlgorithm: 'Random', totalSlots: 4, activeSlots: 0 },
  { id: 'gp', name: 'Goeppingen', isActive: true, defaultAlgorithm: 'Random', totalSlots: 10, activeSlots: 10 },
  { id: 'ef', name: 'Erfurt', isActive: true, defaultAlgorithm: 'Random', totalSlots: 6, activeSlots: 6 },
];

/**
 * Slots, grid layout and blocked days are per-location admin tools. A deactivated location
 * is managed only under Locations, so these dropdowns offer active locations only.
 */
describe('admin location dropdowns list active locations only', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(adminService.getLocations).mockResolvedValue(locations);
    vi.mocked(adminService.getSlots).mockResolvedValue([]);
    vi.mocked(adminService.getBlockedDays).mockResolvedValue([]);
  });

  it.each([
    ['Parking slots', <SlotsPage />, 'slots-location', 'gp'],
    ['Blocked days', <BlockedDaysPage />, 'blocked-days-location', 'gp'],
    ['Grid layout', <GridLayoutPage />, 'grid-location', ''],
  ])('%s', async (_name, page, selectId, expectedSelection) => {
    renderWithRouter(page);

    const select = (await screen.findByRole('combobox')) as HTMLSelectElement;
    expect(select.id).toBe(selectId);

    const offered = Array.from(select.options).map((o) => o.value).filter(Boolean);
    expect(offered).toEqual(['gp', 'ef']);
    expect(screen.queryByText('Old Yard (closed)')).not.toBeInTheDocument();

    // The first *active* location is preselected where the page preselects at all.
    await waitFor(() => expect(select.value).toBe(expectedSelection));
  });

  it('Parking slots loads the slots of the first active location, never of an inactive one', async () => {
    renderWithRouter(<SlotsPage />);

    await waitFor(() => expect(adminService.getSlots).toHaveBeenCalledWith('gp'));
    expect(adminService.getSlots).not.toHaveBeenCalledWith('old');
  });
});
