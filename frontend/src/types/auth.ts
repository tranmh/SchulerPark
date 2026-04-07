export interface User {
  id: string;
  email: string;
  displayName: string;
  carLicensePlate: string | null;
  role: 'User' | 'Admin';
  hasAzureAd: boolean;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: User;
}

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  email: string;
  displayName: string;
  password: string;
}

export interface AuthConfig {
  azureAdEnabled: boolean;
  azureAdClientId: string | null;
  azureAdTenantId: string | null;
}
