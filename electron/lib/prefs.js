'use strict';

const fs = require('node:fs');
const path = require('node:path');
const { writeAtomic } = require('./secret');

// The few settings the main process needs before the server answers (window theme, start hidden,
// language for the server). The server's settings.json stays the source of truth.
const DEFAULTS = Object.freeze({
  language: null,
  theme: 'system',
  startMinimized: false,
  closeToTray: true,
});

class Prefs {
  constructor(userDataDir) {
    this.file = path.join(userDataDir, 'window-state.json');
    this.values = { ...DEFAULTS };
    try {
      const stored = JSON.parse(fs.readFileSync(this.file, 'utf8'));
      if (stored && typeof stored === 'object') {
        for (const key of Object.keys(DEFAULTS)) {
          if (key in stored) {
            this.values[key] = stored[key];
          }
        }
      }
    } catch {
      // Missing or broken cache: defaults.
    }
  }

  get(key) {
    return this.values[key];
  }

  update(partial) {
    let changed = false;
    for (const key of Object.keys(DEFAULTS)) {
      if (key in partial && this.values[key] !== partial[key]) {
        this.values[key] = partial[key];
        changed = true;
      }
    }

    if (changed) {
      try {
        fs.mkdirSync(path.dirname(this.file), { recursive: true });
        writeAtomic(this.file, JSON.stringify(this.values, null, 2));
      } catch {
        // Only a cache; the next settings update writes it again.
      }
    }
  }
}

module.exports = { Prefs };
