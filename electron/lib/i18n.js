'use strict';

const fs = require('node:fs');
const path = require('node:path');

const SUPPORTED = ['de', 'en', 'fr', 'es', 'it', 'nl', 'pl', 'ru'];
const catalogDir = path.join(__dirname, '..', 'renderer', 'i18n');

function readCatalog(language) {
  try {
    return JSON.parse(fs.readFileSync(path.join(catalogDir, `${language}.json`), 'utf8'));
  } catch {
    return {};
  }
}

// "{0}" placeholders like .NET string.Format; "{{" and "}}" are literal braces.
function format(text, args) {
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

function resolveLanguage(setting, locale) {
  const wanted = (setting || locale || 'en').toLowerCase().split(/[-_]/)[0];
  return SUPPORTED.includes(wanted) ? wanted : 'en';
}

class MainI18n {
  constructor() {
    this.language = null;
    this.catalogs = [];
  }

  setLanguage(language) {
    if (language === this.language) {
      return;
    }

    this.language = language;
    this.catalogs = [...new Set([language, 'en', 'de'])].map(readCatalog);
  }

  t(key, ...args) {
    for (const catalog of this.catalogs) {
      if (typeof catalog[key] === 'string') {
        return format(catalog[key], args);
      }
    }

    return key;
  }
}

module.exports = { MainI18n, resolveLanguage, format, SUPPORTED };
