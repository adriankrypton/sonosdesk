'use strict';

const assert = require('node:assert/strict');
const { test } = require('node:test');
const { toAccelerator } = require('../lib/accelerator');
const { parseReadyLine } = require('../lib/server');
const { format, resolveLanguage } = require('../lib/i18n');

test('hotkeys become Electron accelerators', () => {
  assert.equal(toAccelerator('Ctrl+Alt+P', 'win32'), 'Ctrl+Alt+P');
  assert.equal(toAccelerator('Ctrl+Alt+P', 'darwin'), 'Control+Alt+P');
  assert.equal(toAccelerator('Ctrl+Alt+Right', 'win32'), 'Ctrl+Alt+Right');
  assert.equal(toAccelerator('Strg+Shift+NumPad5', 'win32'), 'Ctrl+Shift+num5');
  assert.equal(toAccelerator('Win+F9', 'darwin'), 'Super+F9');
  assert.equal(toAccelerator('Ctrl+Comma', 'win32'), 'Ctrl+,');
  assert.equal(toAccelerator('MediaPlayPause', 'win32'), 'MediaPlayPause');
  assert.equal(toAccelerator('MediaNext', 'darwin'), 'MediaNextTrack');
  assert.equal(toAccelerator('F15', 'win32'), 'F15');
});

test('invalid hotkeys are rejected', () => {
  assert.equal(toAccelerator('P', 'win32'), null);
  assert.equal(toAccelerator('Ctrl+Ctrl+P', 'win32'), null);
  assert.equal(toAccelerator('Hyper+P', 'win32'), null);
  assert.equal(toAccelerator('Ctrl+Pause', 'win32'), null);
  assert.equal(toAccelerator('', 'win32'), null);
  assert.equal(toAccelerator(null, 'win32'), null);
});

test('only a loopback ready line is accepted', () => {
  assert.equal(parseReadyLine('SONOSDESK_READY http://127.0.0.1:51234'), 'http://127.0.0.1:51234');
  assert.equal(parseReadyLine('SONOSDESK_READY http://127.0.0.1:51234/'), 'http://127.0.0.1:51234');
  assert.equal(parseReadyLine('SONOSDESK_READY http://192.168.1.2:51234'), null);
  assert.equal(parseReadyLine('SONOSDESK_READY https://127.0.0.1:51234'), null);
  assert.equal(parseReadyLine('info: Now listening on http://127.0.0.1:5000'), null);
});

test('main-process i18n formatting', () => {
  assert.equal(format('Code {0}', [3]), 'Code 3');
  assert.equal(resolveLanguage(null, 'pl-PL'), 'pl');
  assert.equal(resolveLanguage('xx', 'de'), 'en');
});
