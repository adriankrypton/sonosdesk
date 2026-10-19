// Pure helpers (no DOM, no state) so they can be unit-tested with node --test.

export const MEDIA_RENDERER_INPUT = '47';

export function clamp(value, min, max) {
  return Math.min(max, Math.max(min, value));
}

export function formatTime(seconds) {
  if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) {
    return '–:––';
  }

  const total = Math.floor(seconds);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = String(total % 60).padStart(2, '0');
  return h > 0 ? `${h}:${String(m).padStart(2, '0')}:${s}` : `${m}:${s}`;
}

export function nextRepeat(repeat) {
  return repeat === 'off' ? 'all' : repeat === 'all' ? 'one' : 'off';
}

export function isPlayingTransport(transport) {
  return transport === 'playing' || transport === 'transitioning';
}

export function sourceText(playback, t) {
  if (!playback) {
    return null;
  }

  const keys = {
    queue: 'group.source.queue',
    radio: 'group.source.radio',
    streamingService: 'group.source.streaming',
    lineIn: 'group.source.line_in',
    homeTheater: 'group.source.tv',
    airPlay: 'group.source.airplay',
    musicLibrary: 'group.source.library',
    groupMember: 'group.source.group',
    stream: 'group.source.stream',
    none: 'group.source.none',
  };
  const source = keys[playback.source] ? t(keys[playback.source]) : '';
  const name = playback.sourceName;
  if (name && source) {
    return `${source} · ${name}`;
  }

  return name || source || null;
}

// Second line of a Sonos room in the room list (WPF GroupViewModel.Subtitle).
export function groupSubtitle(group, { playing, buffering }, t) {
  if (group.error) {
    return t('group.unreachable');
  }

  const playback = group.playback;
  const title = playback?.displayTitle || null;
  if (buffering) {
    return title ? t('group.buffering_title', title) : t('group.buffering');
  }

  if (title) {
    const artist = playback.displayArtist;
    const text = artist ? `${title} · ${artist}` : title;
    return playing ? text : t('group.paused_title', text);
  }

  switch (playback?.transport) {
    case 'playing':
      return t('group.playing');
    case 'paused':
    case 'stopped':
      return t('group.ready');
    case 'noMedia':
      return t('group.no_media');
    default:
      return playback ? t('group.ready') : t('group.loading');
  }
}

export function receiverDisplay(receiver, { buffering }, t) {
  const networkInput = receiver.inputCode === MEDIA_RENDERER_INPUT;
  const title = receiver.playing ? receiver.stationName : receiver.on && !networkInput ? receiver.inputName : null;
  const artist = receiver.playing ? receiver.songTitle : null;
  const source = receiver.playing
    ? t('receiver.source_radio')
    : receiver.on && receiver.inputName ? t('receiver.source_input', receiver.inputName) : t('receiver.source_receiver');

  let subtitle;
  if (!receiver.reachable) {
    subtitle = t('group.unreachable');
  } else if (!receiver.hasStatus) {
    subtitle = t('group.loading');
  } else if (buffering) {
    subtitle = t('group.buffering');
  } else if (receiver.playing && receiver.stationName) {
    subtitle = receiver.songTitle ? `${receiver.stationName} · ${receiver.songTitle}` : receiver.stationName;
  } else if (receiver.on) {
    subtitle = (networkInput ? null : receiver.inputName) || t('group.ready');
  } else {
    subtitle = t('receiver.status_standby');
  }

  return { title, artist, source, subtitle };
}

// Footer line under the room list (WPF MainViewModel.ApplyDiscoveryStatus).
export function discoveryText(discovery, t, formatClock) {
  if (!discovery) {
    return t('main.discovery.none_yet');
  }

  switch (discovery.phase) {
    case 'searching':
      return t('main.discovery.searching');
    case 'idle':
      return t('main.discovery.starting');
    case 'stopped':
      return t('main.discovery.stopped');
    default:
      break;
  }

  if (!(discovery.online > 0)) {
    return t('main.discovery.none_found');
  }

  let text = discovery.census
    ? t('main.discovery.census', discovery.online, discovery.census.mediaRenderers, discovery.census.otherUpnp)
    : t('main.discovery.online', discovery.online);
  if (discovery.unreachable > 0) {
    text += t('main.discovery.unreachable', discovery.unreachable);
  }

  if (discovery.lastCompletedAt) {
    const date = new Date(discovery.lastCompletedAt);
    if (!Number.isNaN(date.getTime())) {
      text += ` · ${formatClock(date)}`;
    }
  }

  return text;
}

export function hasDiscoveryProblem(discovery) {
  return Boolean(discovery) && (discovery.phase === 'failed' || (!(discovery.online > 0) && Boolean(discovery.error)));
}

export function emptyRoomsText(discovery, t) {
  if (!discovery || ((discovery.phase === 'idle' || discovery.phase === 'searching') && !(discovery.online > 0))) {
    return t('main.empty.searching');
  }

  if (!(discovery.online > 0)) {
    return discovery.error || t('main.empty.none_found');
  }

  return t('main.empty.loading');
}

function ipv4Bytes(text) {
  const parts = text.split('.');
  if (parts.length !== 4 || !parts.every((p) => /^\d{1,3}$/.test(p))) {
    return null;
  }

  const bytes = parts.map(Number);
  return bytes.every((b) => b <= 255) ? bytes : null;
}

// Private IPv4 ranges, like SettingsNormalizer.IsLocalIPv4.
export function isLocalIPv4(text) {
  const bytes = typeof text === 'string' ? ipv4Bytes(text.trim()) : null;
  if (!bytes) {
    return false;
  }

  return bytes[0] === 10 || (bytes[0] === 172 && bytes[1] >= 16 && bytes[1] <= 31) || (bytes[0] === 192 && bytes[1] === 168);
}

// Speaker address: private IPv4, optionally with a port (the server validates again).
export function isSpeakerAddress(text) {
  if (typeof text !== 'string') {
    return false;
  }

  const match = /^([\d.]+)(?::(\d{1,5}))?$/.exec(text.trim());
  if (!match || !isLocalIPv4(match[1])) {
    return false;
  }

  return match[2] === undefined || (Number(match[2]) > 0 && Number(match[2]) <= 65535);
}

export function stationDetails(station, t) {
  const parts = [
    station.isCustom ? t('radio.custom_stream') : null,
    station.codec || null,
    station.bitrate ? t('radio.bitrate', station.bitrate) : null,
    Array.isArray(station.tags) && station.tags.length > 0 ? station.tags.slice(0, 3).join(', ') : null,
  ];
  return parts.filter((p) => p && String(p).trim()).join(' · ');
}

const KEY_NAMES = {
  Space: 'Space',
  PageUp: 'PageUp',
  PageDown: 'PageDown',
  End: 'End',
  Home: 'Home',
  ArrowLeft: 'Left',
  ArrowUp: 'Up',
  ArrowRight: 'Right',
  ArrowDown: 'Down',
  Insert: 'Insert',
  Delete: 'Delete',
  Pause: 'Pause',
  NumpadMultiply: 'Multiply',
  NumpadAdd: 'Add',
  NumpadSubtract: 'Subtract',
  NumpadDivide: 'Divide',
  Equal: 'Plus',
  BracketRight: 'Plus',
  Comma: 'Comma',
  Minus: 'Minus',
  Slash: 'Minus',
  Period: 'Period',
  AudioVolumeMute: 'VolumeMute',
  AudioVolumeDown: 'VolumeDown',
  AudioVolumeUp: 'VolumeUp',
  MediaTrackNext: 'MediaNext',
  MediaTrackPrevious: 'MediaPrevious',
  MediaStop: 'MediaStop',
  MediaPlayPause: 'MediaPlayPause',
};

const STANDALONE_KEYS = new Set(['VolumeMute', 'VolumeDown', 'VolumeUp', 'MediaNext', 'MediaPrevious', 'MediaStop', 'MediaPlayPause']);

function keyName(code) {
  let match = /^Key([A-Z])$/.exec(code);
  if (match) {
    return match[1];
  }

  match = /^Digit([0-9])$/.exec(code);
  if (match) {
    return match[1];
  }

  match = /^Numpad([0-9])$/.exec(code);
  if (match) {
    return `NumPad${match[1]}`;
  }

  if (/^F([1-9]|1[0-9]|2[0-4])$/.test(code)) {
    return code;
  }

  return KEY_NAMES[code] ?? null;
}

// Hotkey text in the settings.json format ("Ctrl+Alt+P") from a keydown event.
// Returns "" to clear (Delete/Backspace without modifiers) and null for keys to ignore.
export function hotkeyFromEvent(event) {
  const modifierOnly = ['Control', 'Alt', 'Shift', 'Meta', 'AltGraph', 'OS'].includes(event.key);
  if (modifierOnly) {
    return null;
  }

  const hasModifier = event.ctrlKey || event.altKey || event.shiftKey || event.metaKey;
  if (!hasModifier && (event.code === 'Delete' || event.code === 'Backspace')) {
    return '';
  }

  const key = keyName(event.code);
  if (!key) {
    return null;
  }

  const parts = [];
  if (event.ctrlKey) {
    parts.push('Ctrl');
  }

  if (event.altKey) {
    parts.push('Alt');
  }

  if (event.shiftKey) {
    parts.push('Shift');
  }

  if (event.metaKey) {
    parts.push('Win');
  }

  if (parts.length === 0 && !STANDALONE_KEYS.has(key) && !/^F(1[3-9]|2[0-4])$/.test(key)) {
    return null;
  }

  parts.push(key);
  return parts.join('+');
}

// Shown in the hotkey fields; on macOS with the Mac symbols.
export function displayHotkey(text, isMac) {
  if (!text) {
    return '';
  }

  if (!isMac) {
    return text;
  }

  const symbols = { Ctrl: '⌃', Alt: '⌥', Shift: '⇧', Win: '⌘', Left: '←', Right: '→', Up: '↑', Down: '↓' };
  return text.split('+').map((part) => symbols[part] ?? part).join('');
}

export const COUNTRY_CODES = (
  'AD AE AF AG AI AL AM AO AQ AR AS AT AU AW AX AZ BA BB BD BE BF BG BH BI BJ BL BM BN BO BQ BR BS BT BV BW BY BZ '
  + 'CA CC CD CF CG CH CI CK CL CM CN CO CR CU CV CW CX CY CZ DE DJ DK DM DO DZ EC EE EG EH ER ES ET FI FJ FK FM FO FR '
  + 'GA GB GD GE GF GG GH GI GL GM GN GP GQ GR GS GT GU GW GY HK HM HN HR HT HU ID IE IL IM IN IO IQ IR IS IT JE JM JO JP '
  + 'KE KG KH KI KM KN KP KR KW KY KZ LA LB LC LI LK LR LS LT LU LV LY MA MC MD ME MF MG MH MK ML MM MN MO MP MQ MR MS MT '
  + 'MU MV MW MX MY MZ NA NC NE NF NG NI NL NO NP NR NU NZ OM PA PE PF PG PH PK PL PM PN PR PS PT PW PY QA RE RO RS RU RW '
  + 'SA SB SC SD SE SG SH SI SJ SK SL SM SN SO SR SS ST SV SX SY SZ TC TD TF TG TH TJ TK TL TM TN TO TR TT TV TW TZ UA UG '
  + 'UM US UY UZ VA VC VE VG VI VN VU WF WS XK YE YT ZA ZM ZW'
).split(' ');
