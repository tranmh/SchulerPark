import type { GridSlot } from '../../types/grid';

interface Props {
  slots: GridSlot[];
  placedSlotIds: Set<string>;
  selectedSlotId: string | null;
  onSelect: (slotId: string) => void;
}

export function SlotPalette({ slots, placedSlotIds, selectedSlotId, onSelect }: Props) {
  const unplaced = slots.filter((s) => !placedSlotIds.has(s.id));
  const placed = slots.filter((s) => placedSlotIds.has(s.id));

  return (
    <div className="rounded-card border border-line bg-white p-4 shadow-card">
      <div className="mb-3 flex items-center justify-between">
        <h3 className="text-[10.5px] font-semibold uppercase tracking-[0.08em] text-ink-400">
          Unplaced slots
        </h3>
        <span className="rounded-full bg-brand-50 px-2 py-0.5 text-[11px] font-semibold text-brand-700 num">
          {unplaced.length}
        </span>
      </div>

      <div className="max-h-64 space-y-1.5 overflow-y-auto pr-1 scroll-thin">
        {unplaced.length === 0 && (
          <p className="rounded-md bg-emerald-50 px-3 py-2 text-[12px] text-emerald-700 ring-1 ring-inset ring-emerald-100">
            ✓ All slots placed on grid.
          </p>
        )}
        {unplaced.map((slot) => (
          <div
            key={slot.id}
            draggable={slot.isActive}
            onDragStart={(e) => {
              e.dataTransfer.setData('slotId', slot.id);
              e.dataTransfer.effectAllowed = 'move';
            }}
            onClick={() => onSelect(slot.id)}
            className={`flex items-center justify-between rounded-md border px-2.5 py-1.5 text-[12px] font-semibold transition-colors ${
              selectedSlotId === slot.id
                ? 'border-brand-500 bg-brand-50 text-brand-800 ring-2 ring-brand-200/60'
                : 'border-line bg-white text-ink-700 hover:border-brand-300 hover:bg-brand-50/40'
            } ${!slot.isActive ? 'opacity-50' : 'cursor-grab'}`}
          >
            <span className="num">{slot.slotNumber}</span>
            <span className="flex items-center gap-1.5 text-[10.5px] font-medium text-ink-400">
              {slot.label && <span>{slot.label}</span>}
              {!slot.isActive && (
                <span className="rounded bg-rose-50 px-1.5 py-0.5 text-[10px] text-rose-700 ring-1 ring-inset ring-rose-200">
                  inactive
                </span>
              )}
            </span>
          </div>
        ))}
      </div>

      {placed.length > 0 && (
        <>
          <h3 className="mt-5 mb-2 text-[10.5px] font-semibold uppercase tracking-[0.08em] text-ink-400">
            Placed ({placed.length})
          </h3>
          <div className="max-h-32 space-y-1 overflow-y-auto pr-1 scroll-thin">
            {placed.map((slot) => (
              <div
                key={slot.id}
                className="flex items-center justify-between rounded-md border border-line bg-surface-sunken px-2.5 py-1 text-[11.5px] text-ink-400"
              >
                <span className="num font-medium text-ink-500">{slot.slotNumber}</span>
                <span className="num">
                  R{slot.row} · C{slot.column}
                </span>
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
