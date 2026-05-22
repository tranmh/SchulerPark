import { useEffect, useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { adminService } from '../../services/adminService';
import { GridEditor } from '../../components/grid/GridEditor';
import { SlotPalette } from '../../components/grid/SlotPalette';
import { CellTypePicker } from '../../components/grid/CellTypePicker';
import type { Tool } from '../../components/grid/CellTypePicker';
import { LoadingSpinner } from '../../components/LoadingSpinner';
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
  const { t } = useTranslation();
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

  const placedSlotIds = new Set<string>(Array.from(placedSlots.values()).map((s) => s.slotId));

  const placeSlotAt = (row: number, col: number, slotId: string) => {
    const key = `${row},${col}`;
    if (placedSlots.has(key) || placedCells.has(key)) return;

    const slot = slots.find((s) => s.id === slotId);
    if (!slot) return;

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
        gridRows,
        gridColumns,
        slotPositions,
        cells,
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

  if (loading) return <LoadingSpinner />;

  const stats = {
    placed: placedSlots.size,
    capacity: gridRows * gridColumns,
    cells: placedCells.size,
  };

  return (
    <div>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h1 className="text-[26px] font-bold tracking-tight text-ink-900">{t('admin.gridLayout')}</h1>
          <p className="mt-1 max-w-2xl text-[13.5px] text-ink-400">
            Place parking slots, roads, obstacles, entrances and labels on a 2D grid. Drag slots from the tray or click cells with
            a tool selected. Right-click to remove.
          </p>
        </div>
        {locationId && (
          <div className="flex items-center gap-2">
            <button
              type="button"
              onClick={handleClear}
              className="rounded-lg border border-line-strong bg-white px-3.5 py-2 text-[13px] font-medium text-ink-700 hover:bg-surface-sunken"
            >
              Clear grid
            </button>
            <button
              type="button"
              onClick={handleSave}
              disabled={saving}
              className="rounded-lg bg-brand-500 px-4 py-2.5 text-[13.5px] font-medium text-white shadow-sm hover:bg-brand-600 disabled:opacity-60"
            >
              {saving ? 'Saving…' : 'Save layout'}
            </button>
          </div>
        )}
      </div>

      {error && (
        <div className="mt-5 rounded-lg border border-rose-200 bg-rose-50 px-3.5 py-3 text-[13px] text-rose-800">{error}</div>
      )}
      {success && (
        <div className="mt-5 flex items-start gap-2.5 rounded-lg border border-emerald-200 bg-emerald-50 px-3.5 py-3 text-[13px] text-emerald-800">
          <svg className="mt-0.5 h-4 w-4 shrink-0" fill="none" stroke="currentColor" viewBox="0 0 24 24">
            <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M5 13l4 4L19 7" />
          </svg>
          {success}
        </div>
      )}

      {/* Controls bar */}
      <div className="mt-6 flex flex-wrap items-end gap-4">
        <div>
          <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">Location</label>
          <select
            id="grid-location"
            value={locationId}
            onChange={(e) => handleLocationChange(e.target.value)}
            className="w-64 rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900"
          >
            <option value="">Select location…</option>
            {locations.map((l) => (
              <option key={l.id} value={l.id}>{l.name}</option>
            ))}
          </select>
        </div>
        {locationId && (
          <>
            <div>
              <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">Rows</label>
              <input
                type="number"
                min={1}
                max={30}
                value={gridRows}
                onChange={(e) => setGridRows(Math.min(30, Math.max(1, +e.target.value)))}
                className="w-20 rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900 num"
              />
            </div>
            <div>
              <label className="mb-1.5 block text-[11px] font-semibold uppercase tracking-wider text-ink-400">Columns</label>
              <input
                type="number"
                min={1}
                max={30}
                value={gridColumns}
                onChange={(e) => setGridColumns(Math.min(30, Math.max(1, +e.target.value)))}
                className="w-20 rounded-lg border border-line-strong bg-white px-3.5 py-2.5 text-[13px] text-ink-900 num"
              />
            </div>
          </>
        )}
      </div>

      {locationId && (
        <div className="mt-7 grid gap-6 lg:grid-cols-[260px_1fr]">
          <div className="space-y-5">
            <CellTypePicker
              selected={selectedTool}
              onChange={(t) => {
                setSelectedTool(t);
                setSelectedSlotId(null);
              }}
            />
            <SlotPalette
              slots={slots}
              placedSlotIds={placedSlotIds}
              selectedSlotId={selectedSlotId}
              onSelect={(id) => {
                setSelectedSlotId(id);
                setSelectedTool('slot');
              }}
            />
          </div>

          <div>
            <div className="mb-3 flex items-center justify-between text-[12.5px] text-ink-400">
              <div>
                <b className="text-ink-700 num">{gridRows}</b> rows × <b className="text-ink-700 num">{gridColumns}</b> columns
              </div>
              <div>
                <b className="text-ink-700 num">{stats.placed}</b> slot{stats.placed !== 1 ? 's' : ''} placed ·{' '}
                <b className="text-ink-700 num">{stats.cells}</b> cell{stats.cells !== 1 ? 's' : ''} marked
              </div>
            </div>
            <GridEditor
              rows={gridRows}
              columns={gridColumns}
              placedSlots={placedSlots}
              placedCells={placedCells}
              onCellClick={handleCellClick}
              onCellDrop={handleCellDrop}
              onCellRightClick={handleCellRightClick}
            />
            <p className="mt-3 text-[11.5px] text-ink-400">
              <b className="text-ink-700">Left-click</b> places · <b className="text-ink-700">Right-click</b> removes ·{' '}
              <b className="text-ink-700">Drag</b> slots from the tray to position them.
            </p>
          </div>
        </div>
      )}
    </div>
  );
}
