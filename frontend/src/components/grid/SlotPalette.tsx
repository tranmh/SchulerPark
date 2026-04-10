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
    <div className="space-y-3">
      <h3 className="text-sm font-semibold text-gray-700">Unplaced Slots ({unplaced.length})</h3>
      <div className="max-h-64 space-y-1 overflow-y-auto">
        {unplaced.length === 0 && (
          <p className="text-xs text-gray-400">All slots placed on grid</p>
        )}
        {unplaced.map((slot) => (
          <div
            key={slot.id}
            draggable
            onDragStart={(e) => {
              e.dataTransfer.setData('slotId', slot.id);
              e.dataTransfer.effectAllowed = 'move';
            }}
            onClick={() => onSelect(slot.id)}
            className={`cursor-grab rounded border px-2 py-1.5 text-xs font-medium transition-colors ${
              selectedSlotId === slot.id
                ? 'border-blue-500 bg-blue-50 text-blue-800'
                : 'border-gray-200 bg-white text-gray-700 hover:border-blue-300 hover:bg-blue-50'
            } ${!slot.isActive ? 'opacity-50' : ''}`}
          >
            {slot.slotNumber}
            {slot.label && <span className="ml-1 text-gray-400">({slot.label})</span>}
            {!slot.isActive && <span className="ml-1 text-red-400">(inactive)</span>}
          </div>
        ))}
      </div>
      {placed.length > 0 && (
        <>
          <h3 className="text-sm font-semibold text-gray-500">Placed ({placed.length})</h3>
          <div className="max-h-32 space-y-1 overflow-y-auto">
            {placed.map((slot) => (
              <div
                key={slot.id}
                className="rounded border border-gray-100 bg-gray-50 px-2 py-1 text-xs text-gray-400"
              >
                {slot.slotNumber} - Row {slot.row}, Col {slot.column}
              </div>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
