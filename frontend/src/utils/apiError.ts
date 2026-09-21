import i18n from '../i18n';

/**
 * Error payloads the API sends: RFC 9457 ProblemDetails from the exception
 * middleware (`detail` + optional `code` extension) and the auth controller's
 * `{ error, code }` objects.
 */
export interface ApiErrorData {
  code?: string;
  detail?: string;
  error?: string;
}

export function getApiErrorData(err: unknown): ApiErrorData | undefined {
  return (err as { response?: { data?: ApiErrorData } } | null)?.response?.data;
}

export function getApiErrorCode(err: unknown): string | undefined {
  return getApiErrorData(err)?.code;
}

/**
 * Localized message for an API error. A known `code` maps to
 * `apiErrors.<code>` in the current language; otherwise the server's English
 * text is shown (still more useful than nothing), and if there is none the
 * caller's generic fallback key is used.
 */
export function describeApiError(err: unknown, fallbackKey: string): string {
  const data = getApiErrorData(err);
  if (data?.code && i18n.exists(`apiErrors.${data.code}`)) {
    return i18n.t(`apiErrors.${data.code}`);
  }
  return data?.detail || data?.error || i18n.t(fallbackKey);
}
