import type { GridAvailability, SlotStatus, GridCellType } from '../../types/grid';

interface Props {
  availability: GridAvailability;
}

const statusStyles: Record<SlotStatus, string> = {
  Free:     'bg-emerald-50 border-emerald-300 text-emerald-800',
  Booked:   'bg-rose-50 border-rose-300 text-rose-800',
  Blocked:  'bg-ink-100 border-ink-200 text-ink-500',
  Own:      'bg-brand-100 border-brand-400 text-brand-800 ring-2 ring-brand-300/50',
  Inactive: 'bg-line/50 border-line text-ink-300 opacity-60',
};

const cellTypeStyles: Record<GridCellType, string> = {
  Empty:    'bg-white border-line border-dashed',
  Road:     'bg-ink-500 border-ink-600',
  Obstacle: 'bg-ink-800 border-ink-900',
  Entrance: 'bg-brand-300 border-brand-400 text-white',
  Label:    'bg-yellow-100 border-yellow-300 text-yellow-900',
};

const legendItems: { status: SlotStatus; label: string }[] = [
  { status: 'Free',    label: 'Available' },
  { status: 'Booked',  label: 'Booked' },
  { status: 'Own',     label: 'Your booking' },
  { status: 'Blocked', label: 'Blocked' },
];

export function ParkingGridView({ availability }: Props) {
  const { gridRows, gridColumns, slots, cells } = availability;

  const slotMap = new Map(slots.map((s) => [`${s.row},${s.column}`, s]));
  const cellMap = new Map(cells.map((c) => [`${c.row},${c.column}`, c]));

  const gridCells: { row: number; col: number }[] = [];
  for (let r = 0; r < gridRows; r++) {
    for (let c = 0; c < gridColumns; c++) {
      gridCells.push({ row: r, col: c });
    }
  }

  return (
    <div>
      <div className="overflow-auto rounded-card border border-line bg-surface-warm p-3 scroll-thin">
        <div
          className="grid gap-1.5"
          style={{ gridTemplateColumns: `repeat(${gridColumns}, minmax(44px, 1fr))` }}
        >
          {gridCells.map(({ row, col }) => {
            const key = `${row},${col}`;
            const slot = slotMap.get(key);
            const cell = cellMap.get(key);

            let className =
              'flex min-h-[44px] items-center justify-center rounded-md border text-[11px] font-semibold num ';
            let content: React.ReactNode = '';

            if (slot) {
              className += statusStyles[slot.status];
              content = slot.slotNumber;
            } else if (cell) {
              className += cellTypeStyles[cell.cellType];
              if (cell.cellType === 'Label') content = cell.label ?? '';
              if (cell.cellType === 'Entrance') content = '\u2B95';
            } else {
              className += 'bg-white border-line';
            }

            return (
              <div
                key={key}
                className={className}
                title={
                  slot
                    ? `${slot.slotNumber}${slot.label ? ` (${slot.label})` : ''} - ${slot.status}`
                    : undefined
                }
              >
                {content}
              </div>
            );
          })}
        </div>
      </div>

      <div className="mt-3 flex flex-wrap items-center gap-x-4 gap-y-1.5">
        {legendItems.map(({ status, label }) => (
          <div key={status} className="flex items-center gap-1.5">
            <span className={`h-3 w-3 rounded-sm border ${statusStyles[status]}`} />
            <span className="text-[11.5px] text-ink-500">{label}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
