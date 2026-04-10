import type { GridCellType } from '../../types/grid';

interface PlacedSlot {
  slotId: string;
  slotNumber: string;
  label: string | null;
}

interface PlacedCell {
  cellType: GridCellType;
  label: string | null;
}

interface Props {
  rows: number;
  columns: number;
  placedSlots: Map<string, PlacedSlot>;
  placedCells: Map<string, PlacedCell>;
  onCellClick: (row: number, col: number) => void;
  onCellDrop: (row: number, col: number, slotId: string) => void;
  onCellRightClick: (row: number, col: number) => void;
}

const cellTypeStyles: Record<GridCellType, string> = {
  Empty: 'bg-white border-gray-200',
  Road: 'bg-gray-600 border-gray-700',
  Obstacle: 'bg-gray-800 border-gray-900',
  Entrance: 'bg-emerald-200 border-emerald-400',
  Label: 'bg-yellow-100 border-yellow-400',
};

const cellTypeText: Record<GridCellType, string> = {
  Empty: '',
  Road: '',
  Obstacle: '',
  Entrance: '',
  Label: '',
};

export function GridEditor({
  rows, columns, placedSlots, placedCells,
  onCellClick, onCellDrop, onCellRightClick,
}: Props) {
  const cells: { row: number; col: number }[] = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < columns; c++) {
      cells.push({ row: r, col: c });
    }
  }

  return (
    <div className="overflow-auto rounded-lg border border-gray-300 bg-gray-100 p-2">
      <div
        className="grid gap-1"
        style={{
          gridTemplateColumns: `repeat(${columns}, minmax(48px, 1fr))`,
        }}
      >
        {cells.map(({ row, col }) => {
          const key = `${row},${col}`;
          const slot = placedSlots.get(key);
          const cell = placedCells.get(key);

          let className = 'flex min-h-[48px] items-center justify-center rounded border text-xs font-medium transition-colors ';
          let content = '';

          if (slot) {
            className += 'bg-blue-100 border-blue-400 text-blue-800 cursor-pointer';
            content = slot.slotNumber;
          } else if (cell) {
            className += cellTypeStyles[cell.cellType] + ' cursor-pointer';
            content = cell.cellType === 'Label' ? (cell.label ?? 'Label') : cellTypeText[cell.cellType];
            if (cell.cellType === 'Entrance') content = '\u2B95';
          } else {
            className += 'bg-white border-gray-200 border-dashed cursor-pointer hover:bg-gray-50';
          }

          return (
            <div
              key={key}
              className={className}
              title={slot ? `${slot.slotNumber}${slot.label ? ` (${slot.label})` : ''} [${row},${col}]` : `[${row},${col}]`}
              onClick={() => onCellClick(row, col)}
              onContextMenu={(e) => {
                e.preventDefault();
                onCellRightClick(row, col);
              }}
              onDragOver={(e) => {
                e.preventDefault();
                e.dataTransfer.dropEffect = 'move';
              }}
              onDrop={(e) => {
                e.preventDefault();
                const slotId = e.dataTransfer.getData('slotId');
                if (slotId) onCellDrop(row, col, slotId);
              }}
            >
              {content}
            </div>
          );
        })}
      </div>
    </div>
  );
}
