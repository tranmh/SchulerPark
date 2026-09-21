import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { Modal } from './Modal';

describe('Modal', () => {
  it('renders title, subtitle, body and footer', () => {
    render(
      <Modal title="Edit slot" subtitle="Sub" onClose={() => {}} footer={<button type="button">Save</button>}>
        <p>Body text</p>
      </Modal>
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByText('Edit slot')).toBeInTheDocument();
    expect(screen.getByText('Sub')).toBeInTheDocument();
    expect(screen.getByText('Body text')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument();
  });

  it('closes on Escape, close button and backdrop click but not on panel click', () => {
    const onClose = vi.fn();
    render(
      <Modal title="T" onClose={onClose}>
        <p>Body</p>
      </Modal>
    );

    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);

    fireEvent.click(screen.getByRole('button', { name: /schließen|close/i }));
    expect(onClose).toHaveBeenCalledTimes(2);

    fireEvent.click(screen.getByText('Body'));
    expect(onClose).toHaveBeenCalledTimes(2);

    fireEvent.click(screen.getByRole('dialog').parentElement as HTMLElement);
    expect(onClose).toHaveBeenCalledTimes(3);
  });

  it('locks body scroll while mounted and restores it on unmount', () => {
    document.body.style.overflow = '';
    const { unmount } = render(
      <Modal title="T" onClose={() => {}}>
        <p>Body</p>
      </Modal>
    );
    expect(document.body.style.overflow).toBe('hidden');
    unmount();
    expect(document.body.style.overflow).toBe('');
  });
});
