/** Generates a short, unique suffix for test entity names. */
export function uniqueSuffix(): string {
  return `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

/** Returns a name prefixed with `E2E-` and a unique suffix. */
export function uniqueName(prefix: string): string {
  return `E2E-${prefix}-${uniqueSuffix()}`;
}

/** Today / tomorrow as YYYY-MM-DD in Europe/Berlin. */
function localDate(offsetDays = 0): string {
  const d = new Date();
  d.setDate(d.getDate() + offsetDays);
  // server stores DateOnly; we don't need timezone-perfect — local YYYY-MM-DD is fine.
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

export const today = () => localDate(0);
export const tomorrow = () => localDate(1);
export const inDays = (n: number) => localDate(n);

/** Find next Monday (>= 1 day from now), formatted YYYY-MM-DD. */
export function nextMonday(): string {
  const d = new Date();
  d.setDate(d.getDate() + 1);
  while (d.getDay() !== 1) d.setDate(d.getDate() + 1);
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}
