import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { GridLayoutPage } from './GridLayoutPage';
import type { AdminLocation } from '../../types/admin';
import type { GridConfiguration, GridSlot } from '../../types/grid';

vi.mock('../../services/adminService', () => ({
  adminService: {
    getLocations: vi.fn(),
    getGridConfiguration: vi.fn(),
    saveGridConfiguration: vi.fn(),
  },
}));

import { adminService } from '../../services/adminService';

const loc = (id: string, name: string, isActive = true): AdminLocation => ({
  id, name, isActive, defaultAlgorithm: 'PureRandom', totalSlots: 3, activeSlots: 3,
});

const gridSlot = (id: string, slotNumber: string, extra: Partial<GridSlot> = {}): GridSlot => ({
  id, slotNumber, label: null, isActive: true, row: null, column: null, ...extra,
});

const config = (extra: Partial<GridConfiguration> = {}): GridConfiguration => ({
  gridRows: 2,
  gridColumns: 3,
  slots: [
    gridSlot('s1', 'P001', { row: 0, column: 0 }),
    gridSlot('s2', 'P002'),
    gridSlot('s3', 'P003', { isActive: false }),
  ],
  cells: [{ id: 'c1', row: 1, column: 2, cellType: 'Entrance', label: null }],
  ...extra,
});

/** A grid cell is addressed by its title: "[row,col]" when empty, "P001 [row,col]" with a slot. */
const cell = (row: number, col: number) => screen.getByTitle(`[${row},${col}]`);
const slotCell = (slotNumber: string, row: number, col: number) => screen.getByTitle(`${slotNumber} [${row},${col}]`);
const tool = (name: string) => screen.getByRole('button', { name });
const paletteItem = (slotNumber: string) =>
  within(screen.getByText('Unplaced slots').closest('div')!.parentElement as HTMLElement).getByText(slotNumber);

async function openLocation(user: ReturnType<typeof userEvent.setup>, id = 'l1') {
  await user.selectOptions(await screen.findByLabelText('Location'), id);
  await waitFor(() => expect(adminService.getGridConfiguration).toHaveBeenCalledWith(id));
}

describe('GridLayoutPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(adminService.getLocations).mockResolvedValue([loc('l0', 'Old', false), loc('l1', 'Nord'), loc('l2', 'Süd')]);
    vi.mocked(adminService.getGridConfiguration).mockResolvedValue(config());
    vi.mocked(adminService.saveGridConfiguration).mockResolvedValue(config());
  });
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('offers only active locations and shows no editor until one is chosen', async () => {
    render(<GridLayoutPage />);

    const select = await screen.findByLabelText('Location');
    expect(within(select).getAllByRole('option').map((o) => o.textContent)).toEqual(['Select location…', 'Nord', 'Süd']);
    expect(select).toHaveValue('');
    expect(screen.queryByRole('button', { name: 'Save layout' })).not.toBeInTheDocument();
    expect(screen.queryByLabelText('Rows')).not.toBeInTheDocument();
    expect(adminService.getGridConfiguration).not.toHaveBeenCalled();
  });

  it('shows an error when the locations cannot be loaded', async () => {
    vi.mocked(adminService.getLocations).mockRejectedValue(new Error('500'));
    render(<GridLayoutPage />);

    expect(await screen.findByText('Failed to load locations.')).toBeInTheDocument();
  });

  it('loads the saved layout: size, placed slots, cells and the palette', async () => {
    const user = userEvent.setup();
    render(<GridLayoutPage />);

    await openLocation(user);

    expect(screen.getByLabelText('Rows')).toHaveValue(2);
    expect(screen.getByLabelText('Columns')).toHaveValue(3);
    expect(screen.getByText(/rows ×/)).toHaveTextContent('2 rows × 3 columns');
    expect(screen.getByText(/placed ·/)).toHaveTextContent('1 slot placed · 1 cell marked');

    expect(slotCell('P001', 0, 0)).toHaveTextContent('P001');
    expect(cell(1, 2)).toHaveTextContent('⮕');
    expect(cell(0, 1)).toBeEmptyDOMElement();
    // 2 x 3 = 6 cells, no more.
    expect(screen.getAllByTitle(/\[\d+,\d+\]$/)).toHaveLength(6);

    expect(screen.getByText('Unplaced slots')).toBeInTheDocument();
    expect(paletteItem('P002')).toBeInTheDocument();
    expect(paletteItem('P003')).toBeInTheDocument();
    expect(screen.getByText('inactive')).toBeInTheDocument();
    expect(screen.getByText('Placed (1)')).toBeInTheDocument();
  });

  it('falls back to a 5 × 8 grid when the location has no saved size', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.getGridConfiguration).mockResolvedValue(config({ gridRows: null, gridColumns: null, slots: [], cells: [] }));
    render(<GridLayoutPage />);

    await openLocation(user);

    expect(screen.getByLabelText('Rows')).toHaveValue(5);
    expect(screen.getByLabelText('Columns')).toHaveValue(8);
    expect(screen.getAllByTitle(/\[\d+,\d+\]$/)).toHaveLength(40);
    expect(screen.getByText('✓ All slots placed on grid.')).toBeInTheDocument();
  });

  it('shows an error when the layout cannot be loaded', async () => {
    const user = userEvent.setup();
    vi.mocked(adminService.getGridConfiguration).mockRejectedValue(new Error('500'));
    render(<GridLayoutPage />);

    await openLocation(user);

    expect(await screen.findByText('Failed to load grid configuration.')).toBeInTheDocument();
  });

  it('clears the editor when the location is deselected', async () => {
    const user = userEvent.setup();
    render(<GridLayoutPage />);

    await openLocation(user);
    await user.selectOptions(screen.getByLabelText('Location'), '');

    expect(screen.queryByLabelText('Rows')).not.toBeInTheDocument();
    expect(screen.queryByTitle('P001 [0,0]')).not.toBeInTheDocument();
    expect(adminService.getGridConfiguration).toHaveBeenCalledTimes(1);
  });

  it('clamps rows and columns to 1..30 and resizes the grid', async () => {
    const user = userEvent.setup();
    render(<GridLayoutPage />);
    await openLocation(user);

    fireEvent.change(screen.getByLabelText('Rows'), { target: { value: '50' } });
    expect(screen.getByLabelText('Rows')).toHaveValue(30);
    fireEvent.change(screen.getByLabelText('Rows'), { target: { value: '0' } });
    expect(screen.getByLabelText('Rows')).toHaveValue(1);
    fireEvent.change(screen.getByLabelText('Columns'), { target: { value: '2' } });

    expect(screen.getByText(/rows ×/)).toHaveTextContent('1 rows × 2 columns');
    expect(screen.getAllByTitle(/\[\d+,\d+\]$/)).toHaveLength(2);
  });

  describe('editing', () => {
    it('places a selected palette slot on an empty cell', async () => {
      const user = userEvent.setup();
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(paletteItem('P002'));
      await user.click(cell(1, 1));

      expect(slotCell('P002', 1, 1)).toHaveTextContent('P002');
      expect(screen.getByText(/placed ·/)).toHaveTextContent('2 slots placed · 1 cell marked');
      expect(screen.getByText('Placed (2)')).toBeInTheDocument();
      // Selection is consumed: a second click does not place anything else.
      await user.click(cell(0, 1));
      expect(cell(0, 1)).toBeEmptyDOMElement();
    });

    it('moves a slot that is already placed instead of duplicating it', async () => {
      const user = userEvent.setup();
      render(<GridLayoutPage />);
      await openLocation(user);

      fireEvent.drop(cell(0, 2), { dataTransfer: { getData: () => 's1' } });

      expect(slotCell('P001', 0, 2)).toBeInTheDocument();
      expect(screen.queryByTitle('P001 [0,0]')).not.toBeInTheDocument();
      expect(cell(0, 0)).toBeEmptyDOMElement();
      expect(screen.getByText(/placed ·/)).toHaveTextContent('1 slot placed');
    });

    it('refuses to place a slot on an occupied cell or an unknown slot id', async () => {
      const user = userEvent.setup();
      render(<GridLayoutPage />);
      await openLocation(user);

      fireEvent.drop(slotCell('P001', 0, 0), { dataTransfer: { getData: () => 's2' } });
      fireEvent.drop(cell(1, 2), { dataTransfer: { getData: () => 's2' } });
      fireEvent.drop(cell(0, 1), { dataTransfer: { getData: () => 'nope' } });
      fireEvent.drop(cell(0, 1), { dataTransfer: { getData: () => '' } });

      expect(slotCell('P001', 0, 0)).toBeInTheDocument();
      expect(cell(1, 2)).toHaveTextContent('⮕');
      expect(cell(0, 1)).toBeEmptyDOMElement();
      expect(screen.getByText(/placed ·/)).toHaveTextContent('1 slot placed');
    });

    it('marks cells with the chosen cell type but never over a slot', async () => {
      const user = userEvent.setup();
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(tool('Road'));
      await user.click(cell(0, 1));
      await user.click(slotCell('P001', 0, 0));

      expect(cell(0, 1)).toHaveClass('bg-ink-500');
      expect(slotCell('P001', 0, 0)).toHaveTextContent('P001');
      expect(screen.getByText(/placed ·/)).toHaveTextContent('1 slot placed · 2 cells marked');

      // Overwriting a marked cell with another type replaces it.
      await user.click(tool('Obstacle'));
      await user.click(cell(0, 1));
      expect(cell(0, 1)).toHaveClass('bg-ink-800');
      expect(screen.getByText(/placed ·/)).toHaveTextContent('2 cells marked');
    });

    it('asks for the text when placing a Label and shows it in the cell', async () => {
      const user = userEvent.setup();
      const prompt = vi.spyOn(window, 'prompt').mockReturnValueOnce('Exit').mockReturnValueOnce(null);
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(tool('Label'));
      await user.click(cell(0, 1));
      await user.click(cell(0, 2));

      expect(prompt).toHaveBeenCalledWith('Enter label text:');
      expect(cell(0, 1)).toHaveTextContent('Exit');
      expect(cell(0, 2)).toHaveClass('bg-yellow-100');
      expect(cell(0, 2)).toHaveTextContent('');
    });

    it('selecting a tool drops the palette selection, selecting a slot switches back to the slot tool', async () => {
      const user = userEvent.setup();
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(paletteItem('P002'));
      await user.click(tool('Road'));
      await user.click(tool('Slot'));
      await user.click(cell(1, 1));
      expect(cell(1, 1)).toBeEmptyDOMElement();

      await user.click(tool('Road'));
      await user.click(paletteItem('P002'));
      await user.click(cell(1, 1));
      expect(slotCell('P002', 1, 1)).toBeInTheDocument();
    });

    it('the eraser and a right-click remove slots and cells', async () => {
      const user = userEvent.setup();
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(tool('Eraser'));
      await user.click(slotCell('P001', 0, 0));
      expect(cell(0, 0)).toBeEmptyDOMElement();
      expect(paletteItem('P001')).toBeInTheDocument();

      fireEvent.contextMenu(cell(1, 2));
      expect(cell(1, 2)).toBeEmptyDOMElement();
      expect(screen.getByText(/placed ·/)).toHaveTextContent('0 slots placed · 0 cells marked');
    });

    it('"Clear grid" empties everything', async () => {
      const user = userEvent.setup();
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(screen.getByRole('button', { name: 'Clear grid' }));

      expect(screen.getByText(/placed ·/)).toHaveTextContent('0 slots placed · 0 cells marked');
      expect(screen.queryByTitle('P001 [0,0]')).not.toBeInTheDocument();
      expect(screen.queryByText(/Placed \(/)).not.toBeInTheDocument();
    });
  });

  describe('saving', () => {
    it('sends size, slot positions and cells for the location and confirms', async () => {
      const user = userEvent.setup();
      vi.spyOn(window, 'prompt').mockReturnValue('Exit');
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(paletteItem('P002'));
      await user.click(cell(1, 1));
      await user.click(tool('Label'));
      await user.click(cell(0, 2));
      fireEvent.change(screen.getByLabelText('Rows'), { target: { value: '3' } });
      await user.click(screen.getByRole('button', { name: 'Save layout' }));

      expect(adminService.saveGridConfiguration).toHaveBeenCalledWith('l1', {
        gridRows: 3,
        gridColumns: 3,
        slotPositions: [
          { slotId: 's1', row: 0, column: 0 },
          { slotId: 's2', row: 1, column: 1 },
        ],
        cells: [
          { row: 1, column: 2, cellType: 'Entrance', label: undefined },
          { row: 0, column: 2, cellType: 'Label', label: 'Exit' },
        ],
      });
      expect(await screen.findByText('Grid layout saved successfully.')).toBeInTheDocument();
    });

    it('saves an empty layout after clearing', async () => {
      const user = userEvent.setup();
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(screen.getByRole('button', { name: 'Clear grid' }));
      await user.click(screen.getByRole('button', { name: 'Save layout' }));

      expect(adminService.saveGridConfiguration).toHaveBeenCalledWith('l1', {
        gridRows: 2, gridColumns: 3, slotPositions: [], cells: [],
      });
    });

    it('shows the server detail when saving fails with one', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.saveGridConfiguration).mockRejectedValue({
        response: { status: 400, data: { detail: 'Slot P001 lies outside the grid.' } },
      });
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(screen.getByRole('button', { name: 'Save layout' }));

      expect(await screen.findByText('Slot P001 lies outside the grid.')).toBeInTheDocument();
      expect(screen.queryByText('Grid layout saved successfully.')).not.toBeInTheDocument();
    });

    it('shows a generic error when saving fails without a detail', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.saveGridConfiguration).mockRejectedValue(new Error('network'));
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(screen.getByRole('button', { name: 'Save layout' }));

      expect(await screen.findByText('Failed to save grid layout.')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Save layout' })).toBeEnabled();
    });

    it('a later successful save clears the previous error', async () => {
      const user = userEvent.setup();
      vi.mocked(adminService.saveGridConfiguration).mockRejectedValueOnce(new Error('network'));
      render(<GridLayoutPage />);
      await openLocation(user);

      await user.click(screen.getByRole('button', { name: 'Save layout' }));
      expect(await screen.findByText('Failed to save grid layout.')).toBeInTheDocument();

      await user.click(screen.getByRole('button', { name: 'Save layout' }));
      expect(await screen.findByText('Grid layout saved successfully.')).toBeInTheDocument();
      expect(screen.queryByText('Failed to save grid layout.')).not.toBeInTheDocument();
    });
  });
});
