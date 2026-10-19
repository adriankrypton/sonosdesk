export const SUPPORTED_LANGUAGES = ['de', 'en', 'fr', 'es', 'it', 'nl', 'pl', 'ru'];

export const LANGUAGE_NAMES = {
  de: 'Deutsch',
  en: 'English',
  fr: 'Français',
  es: 'Español',
  it: 'Italiano',
  nl: 'Nederlands',
  pl: 'Polski',
  ru: 'Русский',
};

let catalogs = [];
let language = 'en';

// "{0}" placeholders like .NET string.Format (format specifiers are ignored); "{{" / "}}" are braces.
export function format(text, args) {
  return text.replace(/\{\{|\}\}|\{(\d+)(?:[,:][^}]*)?\}/g, (match, index) => {
    if (match === '{{') {
      return '{';
    }

    if (match === '}}') {
      return '}';
    }

    const value = args[Number(index)];
    return value === undefined || value === null ? '' : String(value);
  });
}

export function resolveLanguage(setting, locale) {
  const wanted = String(setting || locale || 'en').toLowerCase().split(/[-_]/)[0];
  return SUPPORTED_LANGUAGES.includes(wanted) ? wanted : 'en';
}

async function fetchCatalog(code) {
  try {
    const response = await fetch(`i18n/${code}.json`);
    return response.ok ? await response.json() : {};
  } catch {
    return {};
  }
}

export async function loadLanguage(setting) {
  const resolved = resolveLanguage(setting, navigator.language);
  if (resolved === language && catalogs.length > 0) {
    return false;
  }

  const codes = [...new Set([resolved, 'en', 'de'])];
  catalogs = await Promise.all(codes.map(fetchCatalog));
  language = resolved;
  document.documentElement.lang = resolved;
  return true;
}

// Fallback chain: selected language → English → German → the key itself.
export function t(key, ...args) {
  for (const catalog of catalogs) {
    const text = catalog[key];
    if (typeof text === 'string') {
      return format(text, args);
    }
  }

  return key;
}

export function currentLanguage() {
  return language;
}
