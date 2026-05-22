import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import en from './locales/en.json';
import de from './locales/de.json';

const STORAGE_KEY = 'schulerpark.lang';
const SUPPORTED = ['de', 'en'] as const;
type Lang = typeof SUPPORTED[number];

function readStoredLang(): Lang {
  try {
    const v = localStorage.getItem(STORAGE_KEY);
    if (v && (SUPPORTED as readonly string[]).includes(v)) return v as Lang;
  } catch {
    // ignore (private mode, etc.)
  }
  return 'de';
}

void i18n.use(initReactI18next).init({
  resources: {
    en: { translation: en },
    de: { translation: de },
  },
  lng: readStoredLang(),
  fallbackLng: 'de',
  interpolation: { escapeValue: false },
  returnNull: false,
});

i18n.on('languageChanged', (lng) => {
  try {
    localStorage.setItem(STORAGE_KEY, lng);
    document.documentElement.lang = lng;
  } catch {
    // ignore
  }
});

document.documentElement.lang = i18n.language;

export default i18n;
