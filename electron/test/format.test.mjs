import assert from 'node:assert/strict';
import { test } from 'node:test';
import {
  discoveryText, displayHotkey, emptyRoomsText, formatTime, groupSubtitle, hotkeyFromEvent, isLocalIPv4, isSpeakerAddress,
  nextRepeat, receiverDisplay, sourceText, stationDetails,
} from '../renderer/js/format.js';
import { format, resolveLanguage } from '../renderer/js/i18n.js';

// Echoes key and arguments so the tests check which text is chosen.
const t = (key, ...args) => (args.length ? `${key}(${args.join('|')})` : key);

test('format replaces string.Format placeholders', () => {
  assert.equal(format('Gruppe · {0} Räume', [3]), 'Gruppe · 3 Räume');
  assert.equal(format('{1} von {0}', ['a', 'b']), 'b von a');
  assert.equal(format('{0:0.#} s', [2.5]), '2.5 s');
  assert.equal(format('{{0}} {0}', ['x']), '{0} x');
  assert.equal(format('{2}', ['a']), '');
});

test('resolveLanguage falls back to English', () => {
  assert.equal(resolveLanguage(null, 'de-DE'), 'de');
  assert.equal(resolveLanguage('fr', 'de-DE'), 'fr');
  assert.equal(resolveLanguage(null, 'ja-JP'), 'en');
  assert.equal(resolveLanguage('', undefined), 'en');
});

test('formatTime', () => {
  assert.equal(formatTime(61), '1:01');
  assert.equal(formatTime(3725), '1:02:05');
  assert.equal(formatTime(null), '–:––');
});

test('repeat cycles off → all → one → off', () => {
  assert.equal(nextRepeat('off'), 'all');
  assert.equal(nextRepeat('all'), 'one');
  assert.equal(nextRepeat('one'), 'off');
});

test('group subtitle follows the WPF rules', () => {
  const group = (playback, error = null) => ({ error, playback });
  assert.equal(groupSubtitle(group(null, 'x'), { playing: false, buffering: false }, t), 'group.unreachable');
  assert.equal(groupSubtitle(group({ displayTitle: 'Song', displayArtist: 'Artist' }), { playing: true, buffering: false }, t), 'Song · Artist');
  assert.equal(groupSubtitle(group({ displayTitle: 'Song' }), { playing: false, buffering: false }, t), 'group.paused_title(Song)');
  assert.equal(groupSubtitle(group({ displayTitle: '1LIVE' }), { playing: true, buffering: true }, t), 'group.buffering_title(1LIVE)');
  assert.equal(groupSubtitle(group({ transport: 'noMedia' }), { playing: false, buffering: false }, t), 'group.no_media');
  assert.equal(groupSubtitle(group(null), { playing: false, buffering: false }, t), 'group.loading');
});

test('source text combines source and name', () => {
  assert.equal(sourceText({ source: 'radio', sourceName: '1LIVE' }, t), 'group.source.radio · 1LIVE');
  assert.equal(sourceText({ source: 'unknown', sourceName: 'Spotify Connect' }, t), 'Spotify Connect');
  assert.equal(sourceText({ source: 'queue', sourceName: null }, t), 'group.source.queue');
  assert.equal(sourceText(null, t), null);
});

test('receiver display', () => {
  const base = { reachable: true, hasStatus: true, on: true, playing: true, stationName: '1LIVE', songTitle: 'A - B', inputCode: '47', inputName: 'SonosDesk' };
  let d = receiverDisplay(base, { buffering: false }, t);
  assert.equal(d.title, '1LIVE');
  assert.equal(d.artist, 'A - B');
  assert.equal(d.source, 'receiver.source_radio');
  assert.equal(d.subtitle, '1LIVE · A - B');
  d = receiverDisplay({ ...base, playing: false, inputCode: '19', inputName: 'HDMI 1' }, { buffering: false }, t);
  assert.equal(d.title, 'HDMI 1');
  assert.equal(d.source, 'receiver.source_input(HDMI 1)');
  d = receiverDisplay({ ...base, playing: false }, { buffering: false }, t);
  assert.equal(d.title, null);
  assert.equal(d.subtitle, 'group.ready');
  assert.equal(receiverDisplay({ ...base, on: false, playing: false }, { buffering: false }, t).subtitle, 'receiver.status_standby');
  assert.equal(receiverDisplay({ ...base, reachable: false }, { buffering: false }, t).subtitle, 'group.unreachable');
});

test('discovery text', () => {
  const clock = () => '12:30';
  assert.equal(discoveryText({ phase: 'searching' }, t, clock), 'main.discovery.searching');
  assert.equal(
    discoveryText({ phase: 'completed', online: 2, unreachable: 1, census: { sonos: 2, mediaRenderers: 1, otherUpnp: 5 }, lastCompletedAt: '2026-01-01T12:30:00Z' }, t, clock),
    'main.discovery.census(2|1|5)main.discovery.unreachable(1) · 12:30',
  );
  assert.equal(discoveryText({ phase: 'completed', online: 0 }, t, clock), 'main.discovery.none_found');
  assert.equal(emptyRoomsText({ phase: 'completed', online: 0, error: 'Firewall' }, t), 'Firewall');
});

test('local addresses', () => {
  assert.ok(isLocalIPv4('192.168.178.42'));
  assert.ok(isLocalIPv4('10.0.0.1'));
  assert.ok(isLocalIPv4('172.20.1.1'));
  assert.ok(!isLocalIPv4('172.32.1.1'));
  assert.ok(!isLocalIPv4('8.8.8.8'));
  assert.ok(!isLocalIPv4('192.168.1'));
  assert.ok(isSpeakerAddress('192.168.178.40:1400'));
  assert.ok(!isSpeakerAddress('192.168.178.40:99999'));
  assert.ok(!isSpeakerAddress('example.com'));
});

test('station details', () => {
  assert.equal(stationDetails({ codec: 'MP3', bitrate: 128, tags: ['pop', 'rock', 'news', 'jazz'] }, t), 'MP3 · radio.bitrate(128) · pop, rock, news');
  assert.equal(stationDetails({ tags: [] }, t), '');
});

test('hotkey capture uses the settings.json format', () => {
  const key = (code, mods = {}) => ({ key: code, code, ctrlKey: false, altKey: false, shiftKey: false, metaKey: false, ...mods });
  assert.equal(hotkeyFromEvent(key('KeyP', { ctrlKey: true, altKey: true })), 'Ctrl+Alt+P');
  assert.equal(hotkeyFromEvent(key('ArrowRight', { ctrlKey: true, altKey: true })), 'Ctrl+Alt+Right');
  assert.equal(hotkeyFromEvent(key('KeyP')), null);
  assert.equal(hotkeyFromEvent(key('Delete')), '');
  assert.equal(hotkeyFromEvent({ ...key('ControlLeft', { ctrlKey: true }), key: 'Control' }), null);
  assert.equal(hotkeyFromEvent(key('MediaPlayPause')), 'MediaPlayPause');
  assert.equal(displayHotkey('Ctrl+Alt+Right', true), '⌃⌥→');
  assert.equal(displayHotkey('Ctrl+Alt+Right', false), 'Ctrl+Alt+Right');
});
