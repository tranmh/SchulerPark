import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { GridEditor } from './GridEditor';

type Props = React.ComponentProps<typeof GridEditor>;

function renderEditor(overrides: Partial<Props> = {}) {
  const props: Props = {
    rows: 2,
    columns: 2,
    placedSlots: new Map([['0,0', { slotId: 's1', slotNumber: 'P001', label: 'Near entrance' }]]),
    placedCells: new Map([
      ['0,1', { cellType: 'Entrance', label: null }],
      ['1,0', { cellType: 'Label', label: 'Exit' }],
    ]),
    onCellClick: vi.fn(),
    onCellDrop: vi.fn(),
    onCellRightClick: vi.fn(),
    ...overrides,
  };
  render(<GridEditor {...props} />);
  return props;
}

describe('GridEditor', () => {
  it('renders rows × columns cells with the slot, cell type and empty content', () => {
    renderEditor();

    expect(screen.getAllByTitle(/\[\d+,\d+\]$/)).toHaveLength(4);
    const slot = screen.getByTitle('P001 (Near entrance) [0,0]');
    expect(slot).toHaveTextContent('P001');
    expect(slot).toHaveClass('bg-brand-100');
    expect(screen.getByTitle('[0,1]')).toHaveTextContent('⮕');
    expect(screen.getByTitle('[1,0]')).toHaveTextContent('Exit');
    expect(screen.getByTitle('[1,0]')).toHaveClass('bg-yellow-100');
    expect(screen.getByTitle('[1,1]')).toBeEmptyDOMElement();
    expect(screen.getByTitle('[1,1]')).toHaveClass('border-dashed');
  });

  it('renders a slot title without the label part when the slot has none', () => {
    renderEditor({ placedSlots: new Map([['1,1', { slotId: 's2', slotNumber: 'P002', label: null }]]) });

    expect(screen.getByTitle('P002 [1,1]')).toBeInTheDocument();
  });

  it('falls back to "Label" for a label cell without text and styles the other types', () => {
    renderEditor({
      placedSlots: new Map(),
      placedCells: new Map([
        ['0,0', { cellType: 'Label', label: null }],
        ['0,1', { cellType: 'Road', label: null }],
        ['1,0', { cellType: 'Obstacle', label: null }],
        ['1,1', { cellType: 'Empty', label: null }],
      ]),
    });

    expect(screen.getByTitle('[0,0]')).toHaveTextContent('Label');
    expect(screen.getByTitle('[0,1]')).toHaveClass('bg-ink-500');
    expect(screen.getByTitle('[1,0]')).toHaveClass('bg-ink-800');
    expect(screen.getByTitle('[1,1]')).toBeEmptyDOMElement();
  });

  it('renders nothing for a zero-sized grid', () => {
    renderEditor({ rows: 0, columns: 5 });

    expect(screen.queryByTitle(/\[\d+,\d+\]$/)).not.toBeInTheDocument();
  });

  it('reports clicks and right-clicks with the cell coordinates', () => {
    const { onCellClick, onCellRightClick } = renderEditor();

    fireEvent.click(screen.getByTitle('[1,1]'));
    expect(onCellClick).toHaveBeenCalledWith(1, 1);

    const contextMenu = fireEvent.contextMenu(screen.getByTitle('P001 (Near entrance) [0,0]'));
    expect(onCellRightClick).toHaveBeenCalledWith(0, 0);
    // The browser context menu is suppressed.
    expect(contextMenu).toBe(false);
  });

  it('reports a drop with the dragged slot id and ignores drops without one', () => {
    const { onCellDrop } = renderEditor();

    fireEvent.drop(screen.getByTitle('[1,1]'), { dataTransfer: { getData: () => 's9' } });
    expect(onCellDrop).toHaveBeenCalledWith(1, 1, 's9');

    fireEvent.drop(screen.getByTitle('[1,1]'), { dataTransfer: { getData: () => '' } });
    expect(onCellDrop).toHaveBeenCalledTimes(1);
  });

  it('accepts drag-over so the drop can happen', () => {
    renderEditor();

    const dragOver = fireEvent.dragOver(screen.getByTitle('[1,1]'), { dataTransfer: { dropEffect: 'none' } });
    expect(dragOver).toBe(false);
  });
});
