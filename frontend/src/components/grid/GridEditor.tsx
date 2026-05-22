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
  Empty:    'bg-white border-line',
  Road:     'bg-ink-500 border-ink-600',
  Obstacle: 'bg-ink-800 border-ink-900',
  Entrance: 'bg-brand-300 border-brand-400 text-white',
  Label:    'bg-yellow-100 border-yellow-300 text-yellow-900',
};

export function GridEditor({
  rows,
  columns,
  placedSlots,
  placedCells,
  onCellClick,
  onCellDrop,
  onCellRightClick,
}: Props) {
  const cells: { row: number; col: number }[] = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < columns; c++) {
      cells.push({ row: r, col: c });
    }
  }

  return (
    <div className="overflow-auto rounded-card border border-line bg-surface-warm p-3 scroll-thin">
      <div
        className="grid gap-1.5"
        style={{
          gridTemplateColumns: `repeat(${columns}, minmax(48px, 1fr))`,
        }}
      >
        {cells.map(({ row, col }) => {
          const key = `${row},${col}`;
          const slot = placedSlots.get(key);
          const cell = placedCells.get(key);

          let className =
            'flex min-h-[48px] items-center justify-center rounded-md border text-[11px] font-semibold num transition-colors ';
          let content: React.ReactNode = '';

          if (slot) {
            className += 'bg-brand-100 border-brand-400 text-brand-800 cursor-pointer hover:bg-brand-200';
            content = slot.slotNumber;
          } else if (cell) {
            className += cellTypeStyles[cell.cellType] + ' cursor-pointer';
            content = cell.cellType === 'Label' ? (cell.label ?? 'Label') : '';
            if (cell.cellType === 'Entrance') content = '\u2B95';
          } else {
            className += 'bg-white border-line border-dashed cursor-pointer hover:bg-brand-50/30 hover:border-brand-300';
          }

          return (
            <div
              key={key}
              className={className}
              title={
                slot
                  ? `${slot.slotNumber}${slot.label ? ` (${slot.label})` : ''} [${row},${col}]`
                  : `[${row},${col}]`
              }
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
