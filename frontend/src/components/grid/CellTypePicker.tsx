import type { GridCellType } from '../../types/grid';

export type Tool = GridCellType | 'slot' | 'eraser';

interface Props {
  selected: Tool;
  onChange: (tool: Tool) => void;
}

interface ToolDef {
  value: Tool;
  label: string;
  swatch: string;
}

const tools: ToolDef[] = [
  { value: 'slot',     label: 'Slot',     swatch: 'bg-brand-200 border-brand-400' },
  { value: 'Road',     label: 'Road',     swatch: 'bg-ink-500 border-ink-600' },
  { value: 'Obstacle', label: 'Obstacle', swatch: 'bg-ink-800 border-ink-900' },
  { value: 'Entrance', label: 'Entrance', swatch: 'bg-brand-300 border-brand-400' },
  { value: 'Label',    label: 'Label',    swatch: 'bg-yellow-100 border-yellow-300' },
  { value: 'eraser',   label: 'Eraser',   swatch: 'bg-white border-line border-dashed' },
];

export function CellTypePicker({ selected, onChange }: Props) {
  return (
    <div className="rounded-card border border-line bg-white p-4 shadow-card">
      <h3 className="mb-3 text-[10.5px] font-semibold uppercase tracking-[0.08em] text-ink-400">
        Tool
      </h3>
      <div className="grid grid-cols-3 gap-2">
        {tools.map(({ value, label, swatch }) => {
          const isActive = selected === value;
          return (
            <button
              key={value}
              type="button"
              onClick={() => onChange(value)}
              className={`flex flex-col items-center gap-1.5 rounded-md px-2 py-2.5 text-[11px] font-semibold transition-all ${
                isActive
                  ? 'border-2 border-brand-500 bg-brand-50 text-brand-700 ring-2 ring-brand-200/50'
                  : 'border border-line bg-white text-ink-600 hover:bg-surface-warm hover:border-line-strong'
              }`}
            >
              <span className={`h-5 w-5 rounded border ${swatch}`} />
              {label}
            </button>
          );
        })}
      </div>
    </div>
  );
}
