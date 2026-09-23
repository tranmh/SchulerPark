export interface User {
  id: string;
  email: string;
  displayName: string;
  carLicensePlate: string | null;
  role: 'User' | 'Admin' | 'SuperAdmin';
  hasAzureAd: boolean;
  preferredLocationId: string | null;
  preferredSlotId: string | null;
  /** 'de' | 'en' — language of emails and push notifications; follows the UI language last used. */
  preferredLanguage: string;
  /** True when the account has a local password (change-password is offered); false for SSO-only accounts. */
  hasPassword: boolean;
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
  /** UI language at registration; decides the language of the verification email. */
  preferredLanguage?: string;
}

export interface AuthConfig {
  azureAdEnabled: boolean;
  azureAdClientId: string | null;
  azureAdTenantId: string | null;
  ssoDomains: string[];
}
