import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen, fireEvent } from '@testing-library/react';
import { Route, Routes } from 'react-router-dom';
import { AppLayout } from './AppLayout';
import { renderWithRouter, createMockAuth, mockUser } from '../test/helpers';
import type { MockAuthValue } from '../test/helpers';

let mockAuth: MockAuthValue & { isSuperAdmin: boolean };

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

function TestApp() {
  return (
    <AppLayout>
      <Routes>
        <Route path="/" element={<div>Dashboard Content</div>} />
        <Route path="/my-bookings" element={<div>My Bookings Content</div>} />
      </Routes>
    </AppLayout>
  );
}

function menuButton() {
  return screen.getByRole('button', { name: /menü öffnen|open menu/i });
}

function drawer() {
  return document.getElementById('app-sidebar') as HTMLElement;
}

describe('AppLayout mobile drawer', () => {
  beforeEach(() => {
    mockAuth = { ...createMockAuth({ isAuthenticated: true, user: mockUser }), isSuperAdmin: false };
    document.body.style.overflow = '';
  });

  it('renders the hamburger with the drawer closed', () => {
    renderWithRouter(<TestApp />);

    expect(menuButton()).toHaveAttribute('aria-expanded', 'false');
    expect(menuButton()).toHaveAttribute('aria-controls', 'app-sidebar');
    expect(drawer().className).toContain('-translate-x-full');
    expect(drawer()).toHaveAttribute('inert');
    expect(document.body.style.overflow).toBe('');
  });

  it('opens the drawer and locks body scroll', () => {
    renderWithRouter(<TestApp />);

    fireEvent.click(menuButton());

    expect(menuButton()).toHaveAttribute('aria-expanded', 'true');
    expect(drawer().className).toContain('translate-x-0');
    expect(drawer().className).not.toContain('-translate-x-full');
    expect(drawer()).not.toHaveAttribute('inert');
    expect(document.body.style.overflow).toBe('hidden');
  });

  it('closes on a nav link click and navigates', () => {
    renderWithRouter(<TestApp />);
    fireEvent.click(menuButton());

    fireEvent.click(screen.getByRole('link', { name: /meine buchungen|my bookings/i }));

    expect(screen.getByText('My Bookings Content')).toBeInTheDocument();
    expect(menuButton()).toHaveAttribute('aria-expanded', 'false');
    expect(document.body.style.overflow).toBe('');
  });

  it('closes on Escape', () => {
    renderWithRouter(<TestApp />);
    fireEvent.click(menuButton());

    fireEvent.keyDown(window, { key: 'Escape' });

    expect(menuButton()).toHaveAttribute('aria-expanded', 'false');
  });

  it('closes on backdrop click and on the close button', () => {
    renderWithRouter(<TestApp />);

    fireEvent.click(menuButton());
    fireEvent.click(screen.getByRole('button', { name: /menü schließen|close menu/i }));
    expect(menuButton()).toHaveAttribute('aria-expanded', 'false');

    fireEvent.click(menuButton());
    const backdrop = document.querySelector('.fixed.inset-0.z-40') as HTMLElement;
    expect(backdrop).not.toBeNull();
    fireEvent.click(backdrop);
    expect(menuButton()).toHaveAttribute('aria-expanded', 'false');
  });

  it('shows admin navigation only for admins', () => {
    renderWithRouter(<TestApp />);
    expect(screen.queryByRole('link', { name: /standorte|locations/i })).not.toBeInTheDocument();

    mockAuth = { ...mockAuth, isAdmin: true };
    renderWithRouter(<TestApp />);
    expect(screen.getByRole('link', { name: /standorte|locations/i })).toBeInTheDocument();
  });
});
