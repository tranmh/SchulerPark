export interface User {
  id: string;
  email: string;
  /** Derived server-side: "First Last" once names are set, legacy free text otherwise. */
  displayName: string;
  /** Phase 21: null for legacy and SSO accounts. */
  firstName: string | null;
  lastName: string | null;
  /** Phase 21: stated at registration, read-only for the user (SuperAdmin can change it). */
  company: string | null;
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
  firstName: string;
  lastName: string;
  /** Phase 21: employer; shown to admins when they approve an external account. */
  company: string;
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
