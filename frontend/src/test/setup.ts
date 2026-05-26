import '@testing-library/jest-dom/vitest';
// Initialise i18next before any component renders. Components that pull from
// useTranslation() (LanguageToggle, BookingStatusBadge, etc.) crash without it.
import '../i18n';
