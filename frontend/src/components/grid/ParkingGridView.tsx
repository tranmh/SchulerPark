import type { GridAvailability, SlotStatus, GridCellType } from '../../types/grid';

interface Props {
  availability: GridAvailability;
}

const statusStyles: Record<SlotStatus, string> = {
  Free: 'bg-green-100 border-green-400 text-green-800',
  Booked: 'bg-red-100 border-red-400 text-red-800',
  Blocked: 'bg-gray-200 border-gray-400 text-gray-500',
  Own: 'bg-blue-100 border-blue-500 text-blue-800 ring-2 ring-blue-300',
  Inactive: 'bg-gray-100 border-gray-300 text-gray-400 opacity-50',
};

const cellTypeStyles: Record<GridCellType, string> = {
  Empty: 'bg-white border-gray-100',
  Road: 'bg-gray-600 border-gray-700',
  Obstacle: 'bg-gray-800 border-gray-900',
  Entrance: 'bg-emerald-200 border-emerald-400',
  Label: 'bg-yellow-100 border-yellow-400',
};

const legendItems: { status: SlotStatus; label: string }[] = [
  { status: 'Free', label: 'Available' },
  { status: 'Booked', label: 'Booked' },
  { status: 'Own', label: 'Your Booking' },
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
      <div className="overflow-auto rounded-lg border border-gray-200 bg-gray-50 p-2">
        <div
          className="grid gap-1"
          style={{ gridTemplateColumns: `repeat(${gridColumns}, minmax(44px, 1fr))` }}
        >
          {gridCells.map(({ row, col }) => {
            const key = `${row},${col}`;
            const slot = slotMap.get(key);
            const cell = cellMap.get(key);

            let className = 'flex min-h-[44px] items-center justify-center rounded border text-xs font-medium ';
            let content = '';

            if (slot) {
              className += statusStyles[slot.status];
              content = slot.slotNumber;
            } else if (cell) {
              className += cellTypeStyles[cell.cellType];
              if (cell.cellType === 'Label') content = cell.label ?? '';
              if (cell.cellType === 'Entrance') content = '\u2B95';
            } else {
              className += 'bg-white border-gray-100';
            }

            return (
              <div
                key={key}
                className={className}
                title={slot ? `${slot.slotNumber}${slot.label ? ` (${slot.label})` : ''} - ${slot.status}` : undefined}
              >
                {content}
              </div>
            );
          })}
        </div>
      </div>

      {/* Legend */}
      <div className="mt-3 flex flex-wrap gap-4">
        {legendItems.map(({ status, label }) => (
          <div key={status} className="flex items-center gap-1.5">
            <div className={`h-3 w-3 rounded border ${statusStyles[status]}`} />
            <span className="text-xs text-gray-600">{label}</span>
          </div>
        ))}
      </div>
    </div>
  );
}
