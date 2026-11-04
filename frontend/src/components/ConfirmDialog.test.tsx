import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ConfirmDialog } from './ConfirmDialog';

function renderDialog(overrides: Partial<React.ComponentProps<typeof ConfirmDialog>> = {}) {
  const onConfirm = vi.fn();
  const onCancel = vi.fn();
  const utils = render(
    <ConfirmDialog
      isOpen
      title="Deactivate slot"
      message="Slot P001 will be deactivated."
      onConfirm={onConfirm}
      onCancel={onCancel}
      {...overrides}
    />
  );
  return { ...utils, onConfirm, onCancel };
}

// B49
describe('ConfirmDialog', () => {
  it('renders nothing while closed', () => {
    const { container } = renderDialog({ isOpen: false });

    expect(container).toBeEmptyDOMElement();
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('shows an alertdialog labelled by its title with the message and default buttons', () => {
    renderDialog();

    const dialog = screen.getByRole('alertdialog', { name: 'Deactivate slot' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(screen.getByText('Slot P001 will be deactivated.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeEnabled();
    expect(screen.getByRole('button', { name: 'Confirm' })).toBeEnabled();
  });

  it('uses custom confirm and cancel labels', () => {
    renderDialog({ confirmLabel: 'Deactivate', cancelLabel: 'Keep' });

    expect(screen.getByRole('button', { name: 'Deactivate' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Keep' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Confirm' })).not.toBeInTheDocument();
  });

  it('calls onConfirm on Confirm and onCancel on Cancel', async () => {
    const user = userEvent.setup();
    const { onConfirm, onCancel } = renderDialog();

    await user.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(onConfirm).toHaveBeenCalledTimes(1);
    expect(onCancel).not.toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onCancel).toHaveBeenCalledTimes(1);
    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('Escape triggers onCancel but other keys do not', () => {
    const { onCancel } = renderDialog();

    fireEvent.keyDown(window, { key: 'Enter' });
    expect(onCancel).not.toHaveBeenCalled();

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('does not react to Escape while closed', () => {
    const { onCancel } = renderDialog({ isOpen: false });

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).not.toHaveBeenCalled();
  });

  it('disables both buttons, shows the processing label and ignores Escape while loading', () => {
    const { onCancel, onConfirm } = renderDialog({ isLoading: true, confirmLabel: 'Deactivate' });

    const cancel = screen.getByRole('button', { name: 'Cancel' });
    const confirm = screen.getByRole('button', { name: 'Processing…' });
    expect(cancel).toBeDisabled();
    expect(confirm).toBeDisabled();
    expect(screen.queryByRole('button', { name: 'Deactivate' })).not.toBeInTheDocument();

    fireEvent.click(cancel);
    fireEvent.click(confirm);
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).not.toHaveBeenCalled();
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('styles the confirm button as danger by default and as primary on request', () => {
    const { unmount } = renderDialog();
    expect(screen.getByRole('button', { name: 'Confirm' })).toHaveClass('bg-rose-600');
    unmount();

    renderDialog({ tone: 'primary' });
    const confirm = screen.getByRole('button', { name: 'Confirm' });
    expect(confirm).toHaveClass('bg-brand-500');
    expect(confirm).not.toHaveClass('bg-rose-600');
  });

  it('locks body scroll while open and restores it when closed', () => {
    document.body.style.overflow = '';
    const { rerender } = renderDialog();
    expect(document.body.style.overflow).toBe('hidden');

    rerender(
      <ConfirmDialog isOpen={false} title="T" message="M" onConfirm={() => {}} onCancel={() => {}} />
    );
    expect(document.body.style.overflow).toBe('');
  });
});
