import { useEffect, useState, useCallback } from 'react';
import { adminService } from '../../services/adminService';
import { GridEditor } from '../../components/grid/GridEditor';
import { SlotPalette } from '../../components/grid/SlotPalette';
import { CellTypePicker } from '../../components/grid/CellTypePicker';
import type { Tool } from '../../components/grid/CellTypePicker';
import type { AdminLocation } from '../../types/admin';
import type { GridSlot, GridCellType, SaveGridConfigurationRequest } from '../../types/grid';

interface PlacedSlot {
  slotId: string;
  slotNumber: string;
  label: string | null;
}

interface PlacedCell {
  cellType: GridCellType;
  label: string | null;
}

export function GridLayoutPage() {
  const [locations, setLocations] = useState<AdminLocation[]>([]);
  const [locationId, setLocationId] = useState<string>('');
  const [slots, setSlots] = useState<GridSlot[]>([]);
  const [gridRows, setGridRows] = useState(5);
  const [gridColumns, setGridColumns] = useState(8);
  const [placedSlots, setPlacedSlots] = useState<Map<string, PlacedSlot>>(new Map());
  const [placedCells, setPlacedCells] = useState<Map<string, PlacedCell>>(new Map());
  const [selectedTool, setSelectedTool] = useState<Tool>('slot');
  const [selectedSlotId, setSelectedSlotId] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    adminService.getLocations()
      .then(setLocations)
      .catch(() => setError('Failed to load locations.'))
      .finally(() => setLoading(false));
  }, []);

  const loadGrid = useCallback(async (locId: string) => {
    setError(null);
    setSuccess(null);
    try {
      const config = await adminService.getGridConfiguration(locId);
      setSlots(config.slots);

      if (config.gridRows != null && config.gridColumns != null) {
        setGridRows(config.gridRows);
        setGridColumns(config.gridColumns);
      } else {
        setGridRows(5);
        setGridColumns(8);
      }

      const sMap = new Map<string, PlacedSlot>();
      for (const s of config.slots) {
        if (s.row != null && s.column != null) {
          sMap.set(`${s.row},${s.column}`, {
            slotId: s.id,
            slotNumber: s.slotNumber,
            label: s.label,
          });
        }
      }
      setPlacedSlots(sMap);

      const cMap = new Map<string, PlacedCell>();
      for (const c of config.cells) {
        cMap.set(`${c.row},${c.column}`, {
          cellType: c.cellType,
          label: c.label,
        });
      }
      setPlacedCells(cMap);
    } catch {
      setError('Failed to load grid configuration.');
    }
  }, []);

  const handleLocationChange = (locId: string) => {
    setLocationId(locId);
    if (locId) loadGrid(locId);
    else {
      setSlots([]);
      setPlacedSlots(new Map());
      setPlacedCells(new Map());
    }
  };

  const placedSlotIds = new Set<string>(
    Array.from(placedSlots.values()).map((s) => s.slotId)
  );

  const placeSlotAt = (row: number, col: number, slotId: string) => {
    const key = `${row},${col}`;
    if (placedSlots.has(key) || placedCells.has(key)) return;

    const slot = slots.find((s) => s.id === slotId);
    if (!slot) return;

    // Remove if already placed elsewhere
    const newSlots = new Map(placedSlots);
    for (const [k, v] of newSlots) {
      if (v.slotId === slotId) {
        newSlots.delete(k);
        break;
      }
    }
    newSlots.set(key, { slotId: slot.id, slotNumber: slot.slotNumber, label: slot.label });
    setPlacedSlots(newSlots);
    setSelectedSlotId(null);
  };

  const handleCellClick = (row: number, col: number) => {
    const key = `${row},${col}`;

    if (selectedTool === 'eraser') {
      const newSlots = new Map(placedSlots);
      const newCells = new Map(placedCells);
      newSlots.delete(key);
      newCells.delete(key);
      setPlacedSlots(newSlots);
      setPlacedCells(newCells);
      return;
    }

    if (selectedTool === 'slot') {
      if (selectedSlotId) {
        placeSlotAt(row, col, selectedSlotId);
      }
      return;
    }

    // Cell type tools
    if (placedSlots.has(key)) return;
    const cellType = selectedTool as GridCellType;
    const label = cellType === 'Label' ? prompt('Enter label text:') ?? '' : null;
    const newCells = new Map(placedCells);
    newCells.set(key, { cellType, label });
    setPlacedCells(newCells);
  };

  const handleCellDrop = (row: number, col: number, slotId: string) => {
    placeSlotAt(row, col, slotId);
  };

  const handleCellRightClick = (row: number, col: number) => {
    const key = `${row},${col}`;
    const newSlots = new Map(placedSlots);
    const newCells = new Map(placedCells);
    newSlots.delete(key);
    newCells.delete(key);
    setPlacedSlots(newSlots);
    setPlacedCells(newCells);
  };

  const handleClear = () => {
    setPlacedSlots(new Map());
    setPlacedCells(new Map());
  };

  const handleSave = async () => {
    if (!locationId) return;
    setSaving(true);
    setError(null);
    setSuccess(null);

    const slotPositions: SaveGridConfigurationRequest['slotPositions'] = [];
    for (const [key, val] of placedSlots) {
      const [row, col] = key.split(',').map(Number);
      slotPositions.push({ slotId: val.slotId, row, column: col });
    }

    const cells: SaveGridConfigurationRequest['cells'] = [];
    for (const [key, val] of placedCells) {
      const [row, col] = key.split(',').map(Number);
      cells.push({ row, column: col, cellType: val.cellType, label: val.label ?? undefined });
    }

    try {
      await adminService.saveGridConfiguration(locationId, {
        gridRows, gridColumns, slotPositions, cells,
      });
      setSuccess('Grid layout saved successfully.');
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { detail?: string } } })?.response?.data?.detail
        ?? 'Failed to save grid layout.';
      setError(msg);
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <div className="flex h-64 items-center justify-center text-gray-400">Loading...</div>;
  }

  return (
    <div>
      <h1 className="text-2xl font-bold text-gray-900">Grid Layout</h1>
      <p className="mt-1 text-sm text-gray-500">
        Configure the 2D parking layout for each location. Drag slots onto the grid or select a tool and click cells.
      </p>

      {error && <div className="mt-4 rounded-md bg-red-50 px-4 py-3 text-sm text-red-700">{error}</div>}
      {success && <div className="mt-4 rounded-md bg-green-50 px-4 py-3 text-sm text-green-700">{success}</div>}

      {/* Location selector + grid dimensions */}
      <div className="mt-6 flex flex-wrap items-end gap-4">
        <div>
          <label className="block text-sm font-medium text-gray-700">Location</label>
          <select
            value={locationId}
            onChange={(e) => handleLocationChange(e.target.value)}
            className="mt-1 rounded-md border border-gray-300 px-3 py-2 text-sm"
          >
            <option value="">Select location...</option>
            {locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}
          </select>
        </div>
        {locationId && (
          <>
            <div>
              <label className="block text-sm font-medium text-gray-700">Rows</label>
              <input
                type="number" min={1} max={30} value={gridRows}
                onChange={(e) => setGridRows(Math.min(30, Math.max(1, +e.target.value)))}
                className="mt-1 w-20 rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700">Columns</label>
              <input
                type="number" min={1} max={30} value={gridColumns}
                onChange={(e) => setGridColumns(Math.min(30, Math.max(1, +e.target.value)))}
                className="mt-1 w-20 rounded-md border border-gray-300 px-3 py-2 text-sm"
              />
            </div>
            <button
              type="button"
              onClick={handleClear}
              className="rounded-md border border-gray-300 px-3 py-2 text-sm font-medium text-gray-700 hover:bg-gray-50"
            >
              Clear Grid
            </button>
            <button
              type="button"
              onClick={handleSave}
              disabled={saving}
              className="rounded-md bg-blue-600 px-4 py-2 text-sm font-medium text-white hover:bg-blue-700 disabled:opacity-50"
            >
              {saving ? 'Saving...' : 'Save Layout'}
            </button>
          </>
        )}
      </div>

      {locationId && (
        <div className="mt-6 flex gap-6">
          {/* Sidebar: slot palette + tool picker */}
          <div className="w-56 shrink-0 space-y-6">
            <div>
              <h3 className="mb-2 text-sm font-semibold text-gray-700">Tool</h3>
              <CellTypePicker selected={selectedTool} onChange={(t) => { setSelectedTool(t); setSelectedSlotId(null); }} />
            </div>
            <SlotPalette
              slots={slots}
              placedSlotIds={placedSlotIds}
              selectedSlotId={selectedSlotId}
              onSelect={(id) => { setSelectedSlotId(id); setSelectedTool('slot'); }}
            />
          </div>

          {/* Grid canvas */}
          <div className="flex-1">
            <GridEditor
              rows={gridRows}
              columns={gridColumns}
              placedSlots={placedSlots}
              placedCells={placedCells}
              onCellClick={handleCellClick}
              onCellDrop={handleCellDrop}
              onCellRightClick={handleCellRightClick}
            />
            <p className="mt-2 text-xs text-gray-400">
              Left-click to place. Right-click to remove. Drag slots from the palette.
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
