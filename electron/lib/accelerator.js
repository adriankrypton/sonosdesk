'use strict';

// Converts SonosDesk hotkey texts ("Ctrl+Alt+P", the format of settings.json) into Electron accelerators.

const MODIFIERS = {
  ctrl: 'ctrl',
  control: 'ctrl',
  strg: 'ctrl',
  alt: 'alt',
  shift: 'shift',
  win: 'super',
  windows: 'super',
};

const KEYS = {
  space: 'Space',
  pageup: 'PageUp',
  pagedown: 'PageDown',
  end: 'End',
  home: 'Home',
  left: 'Left',
  up: 'Up',
  right: 'Right',
  down: 'Down',
  insert: 'Insert',
  delete: 'Delete',
  multiply: 'nummult',
  add: 'numadd',
  subtract: 'numsub',
  divide: 'numdiv',
  plus: 'Plus',
  comma: ',',
  minus: '-',
  period: '.',
  volumemute: 'VolumeMute',
  volumedown: 'VolumeDown',
  volumeup: 'VolumeUp',
  medianext: 'MediaNextTrack',
  mediaprevious: 'MediaPreviousTrack',
  mediastop: 'MediaStop',
  mediaplaypause: 'MediaPlayPause',
};

const STANDALONE = new Set(['VolumeMute', 'VolumeDown', 'VolumeUp', 'MediaNextTrack', 'MediaPreviousTrack', 'MediaStop', 'MediaPlayPause']);

function convertKey(key) {
  const lower = key.toLowerCase();
  if (/^[a-z]$/.test(lower)) {
    return lower.toUpperCase();
  }

  if (/^[0-9]$/.test(lower)) {
    return lower;
  }

  let match = /^numpad([0-9])$/.exec(lower);
  if (match) {
    return `num${match[1]}`;
  }

  match = /^f([1-9]|1[0-9]|2[0-4])$/.exec(lower);
  if (match) {
    return `F${match[1]}`;
  }

  return KEYS[lower] ?? null;
}

// Returns an accelerator string, or null when the text is empty or cannot be represented.
function toAccelerator(text, platform = process.platform) {
  if (typeof text !== 'string' || text.trim() === '') {
    return null;
  }

  const parts = text.split('+').map((p) => p.trim()).filter((p) => p.length > 0);
  if (parts.length === 0) {
    return null;
  }

  const modifiers = new Set();
  for (const part of parts.slice(0, -1)) {
    const modifier = MODIFIERS[part.toLowerCase()];
    if (!modifier || modifiers.has(modifier)) {
      return null;
    }

    modifiers.add(modifier);
  }

  const key = convertKey(parts[parts.length - 1]);
  if (!key || (modifiers.size === 0 && !STANDALONE.has(key) && !/^F(1[3-9]|2[0-4])$/.test(key))) {
    return null;
  }

  const names = [];
  if (modifiers.has('ctrl')) {
    names.push(platform === 'darwin' ? 'Control' : 'Ctrl');
  }

  if (modifiers.has('alt')) {
    names.push('Alt');
  }

  if (modifiers.has('shift')) {
    names.push('Shift');
  }

  if (modifiers.has('super')) {
    names.push('Super');
  }

  names.push(key);
  return names.join('+');
}

module.exports = { toAccelerator };
