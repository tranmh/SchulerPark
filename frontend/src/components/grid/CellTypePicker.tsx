import type { GridCellType } from '../../types/grid';

export type Tool = GridCellType | 'slot' | 'eraser';

interface Props {
  selected: Tool;
  onChange: (tool: Tool) => void;
}

const tools: { value: Tool; label: string; color: string }[] = [
  { value: 'slot', label: 'Slot', color: 'bg-blue-100 border-blue-400 text-blue-800' },
  { value: 'Road', label: 'Road', color: 'bg-gray-600 border-gray-700 text-white' },
  { value: 'Obstacle', label: 'Obstacle', color: 'bg-gray-800 border-gray-900 text-white' },
  { value: 'Entrance', label: 'Entrance', color: 'bg-emerald-200 border-emerald-400 text-emerald-800' },
  { value: 'Label', label: 'Label', color: 'bg-yellow-100 border-yellow-400 text-yellow-800' },
  { value: 'eraser', label: 'Eraser', color: 'bg-white border-gray-300 text-gray-600' },
];

export function CellTypePicker({ selected, onChange }: Props) {
  return (
    <div className="flex flex-wrap gap-2">
      {tools.map(({ value, label, color }) => (
        <button
          key={value}
          type="button"
          onClick={() => onChange(value)}
          className={`rounded-md border px-3 py-1.5 text-xs font-medium transition-all ${color} ${
            selected === value ? 'ring-2 ring-blue-500 ring-offset-1' : ''
          }`}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
