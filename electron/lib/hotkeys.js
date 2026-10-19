'use strict';

const { globalShortcut } = require('electron');
const { toAccelerator } = require('./accelerator');

const HOTKEY_COMMANDS = [
  ['playPause', { type: 'toggle' }],
  ['next', { type: 'next' }],
  ['previous', { type: 'previous' }],
  ['volumeUp', { type: 'volume', direction: 1 }],
  ['volumeDown', { type: 'volume', direction: -1 }],
];

const MEDIA_KEYS = [
  ['MediaPlayPause', { type: 'toggle' }],
  ['MediaNextTrack', { type: 'next' }],
  ['MediaPreviousTrack', { type: 'previous' }],
];

// Registers the global shortcuts from settings.hotkeys plus the keyboard media keys.
// Returns the texts of shortcuts that could not be registered (taken by another app or invalid).
function registerHotkeys(settings, onCommand) {
  globalShortcut.unregisterAll();
  const failed = [];
  const used = new Set();
  const register = (accelerator, command, label) => {
    if (used.has(accelerator)) {
      return;
    }

    try {
      if (globalShortcut.register(accelerator, () => onCommand(command))) {
        used.add(accelerator);
      } else if (label) {
        failed.push(label);
      }
    } catch {
      if (label) {
        failed.push(label);
      }
    }
  };

  const hotkeys = settings?.hotkeys;
  if (hotkeys && hotkeys.enabled !== false) {
    for (const [name, command] of HOTKEY_COMMANDS) {
      const text = hotkeys[name];
      if (!text) {
        continue;
      }

      const accelerator = toAccelerator(text);
      if (accelerator) {
        register(accelerator, command, text);
      } else {
        failed.push(text);
      }
    }
  }

  if (settings?.windowsMediaControls !== false) {
    for (const [accelerator, command] of MEDIA_KEYS) {
      register(accelerator, command, null);
    }
  }

  return failed;
}

function unregisterHotkeys() {
  globalShortcut.unregisterAll();
}

module.exports = { registerHotkeys, unregisterHotkeys };
