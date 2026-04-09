import { render, type RenderOptions } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import type { ReactNode, ReactElement } from 'react';

// Mock auth context value
export interface MockAuthValue {
  user: { id: string; email: string; displayName: string; carLicensePlate: string | null; role: 'User' | 'Admin'; hasAzureAd: boolean } | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  isAdmin: boolean;
  authConfig: { azureAdEnabled: boolean; azureAdClientId: string | null; azureAdTenantId: string | null } | null;
  login: ReturnType<typeof vi.fn>;
  register: ReturnType<typeof vi.fn>;
  loginWithAzureAd: ReturnType<typeof vi.fn>;
  logout: ReturnType<typeof vi.fn>;
}

export const mockUser = {
  id: 'test-user-id',
  email: 'test@schuler.de',
  displayName: 'Test User',
  carLicensePlate: 'GP-TE 1234',
  role: 'User' as const,
  hasAzureAd: false,
};

export const mockAdmin = {
  ...mockUser,
  id: 'admin-id',
  email: 'admin@schulerpark.local',
  displayName: 'Admin',
  role: 'Admin' as const,
};

export function createMockAuth(overrides: Partial<MockAuthValue> = {}): MockAuthValue {
  return {
    user: null,
    isAuthenticated: false,
    isLoading: false,
    isAdmin: false,
    authConfig: { azureAdEnabled: false, azureAdClientId: null, azureAdTenantId: null },
    login: vi.fn(),
    register: vi.fn(),
    loginWithAzureAd: vi.fn(),
    logout: vi.fn(),
    ...overrides,
  };
}

export function renderWithRouter(
  ui: ReactElement,
  { initialEntries = ['/'], ...options }: RenderOptions & { initialEntries?: string[] } = {}
) {
  function Wrapper({ children }: { children: ReactNode }) {
    return <MemoryRouter initialEntries={initialEntries}>{children}</MemoryRouter>;
  }
  return render(ui, { wrapper: Wrapper, ...options });
}
