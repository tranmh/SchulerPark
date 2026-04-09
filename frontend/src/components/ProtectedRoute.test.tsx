import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { Route, Routes } from 'react-router-dom';
import { ProtectedRoute } from './ProtectedRoute';
import { renderWithRouter, createMockAuth, mockUser, mockAdmin } from '../test/helpers';
import type { MockAuthValue } from '../test/helpers';

let mockAuth: MockAuthValue;

vi.mock('../contexts/AuthContext', () => ({
  useAuth: () => mockAuth,
}));

function TestApp({ requireAdmin = false }: { requireAdmin?: boolean }) {
  return (
    <Routes>
      <Route path="/login" element={<div>Login Page</div>} />
      <Route path="/" element={
        <ProtectedRoute requireAdmin={requireAdmin}>
          <div>Protected Content</div>
        </ProtectedRoute>
      } />
    </Routes>
  );
}

describe('ProtectedRoute', () => {
  beforeEach(() => {
    mockAuth = createMockAuth();
  });

  it('shows loading spinner while auth is loading', () => {
    mockAuth = createMockAuth({ isLoading: true });

    renderWithRouter(<TestApp />);

    expect(screen.queryByText('Protected Content')).not.toBeInTheDocument();
    expect(screen.queryByText('Login Page')).not.toBeInTheDocument();
  });

  it('redirects to login when not authenticated', () => {
    mockAuth = createMockAuth({ isAuthenticated: false, isLoading: false });

    renderWithRouter(<TestApp />);

    expect(screen.getByText('Login Page')).toBeInTheDocument();
    expect(screen.queryByText('Protected Content')).not.toBeInTheDocument();
  });

  it('renders children when authenticated', () => {
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockUser, isLoading: false });

    renderWithRouter(<TestApp />);

    expect(screen.getByText('Protected Content')).toBeInTheDocument();
  });

  it('redirects non-admin user from admin route', () => {
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockUser, isAdmin: false, isLoading: false });

    renderWithRouter(<TestApp requireAdmin />);

    // Should redirect to / which in our test setup won't show Protected Content
    // because it requires admin
    expect(screen.queryByText('Protected Content')).not.toBeInTheDocument();
  });

  it('renders admin content for admin user', () => {
    mockAuth = createMockAuth({ isAuthenticated: true, user: mockAdmin, isAdmin: true, isLoading: false });

    renderWithRouter(<TestApp requireAdmin />);

    expect(screen.getByText('Protected Content')).toBeInTheDocument();
  });
});
