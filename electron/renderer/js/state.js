import { isHolding } from './debounce.js';
import { isPlayingTransport } from './format.js';

const STARTING_TIMEOUT_MS = 20_000;
const OPTIMISTIC_MS = 3000;

// Single mutable app state. Mutate, then call invalidate() to schedule a render.
export const state = {
  booted: false,
  bootError: null,
  connected: false,
  server: null,
  app: { version: '', platform: 'win32' },

  version: '',
  backend: '',
  capabilities: new Set(),
  discovery: null,
  groups: [],
  rooms: [],
  receivers: [],
  activeRoom: null,
  settings: null,
  stations: [],

  page: 'nowplaying',
  selection: { kind: null, id: null, room: null },
  lastActiveRoomSent: null,

  // Local overlays on server state (optimistic play state, buffering, local times).
  groupLocal: new Map(),
  receiverLocal: new Map(),
  memberLocal: new Map(),

  favorites: { items: [], loading: false, loaded: false },
  queue: { groupId: null, items: [], total: 0, loading: false, request: 0 },
  sound: { expanded: false, playerId: null, data: null, loading: false, request: 0 },
  receiverSound: { expanded: false, receiverId: null, data: null, loading: false },
  busyChips: new Set(),

  radio: { initialized: false, query: '', country: undefined, results: [], header: null, loading: false, emptyKey: null },
  settingsDraft: null,
  update: { info: null, dismissed: false, checking: false, status: null },
  notice: null,
  dialog: null,
  menu: null,
};

let renderer = null;
let scheduled = false;

export function setRenderer(fn) {
  renderer = fn;
}

export function invalidate() {
  if (scheduled || !renderer) {
    return;
  }

  scheduled = true;
  requestAnimationFrame(() => {
    scheduled = false;
    renderer();
  });
}

export const isMac = () => state.app.platform === 'darwin';

export const has = (capability) => state.capabilities.has(capability);

export function groupById(id) {
  return state.groups.find((g) => g.id === id) ?? null;
}

export function receiverById(id) {
  return state.receivers.find((r) => r.id === id) ?? null;
}

export function selectedGroup() {
  return state.selection.kind === 'group' ? groupById(state.selection.id) : null;
}

export function selectedReceiver() {
  return state.selection.kind === 'receiver' ? receiverById(state.selection.id) : null;
}

// The group the radio page, tray and favorites act on when no receiver is selected.
export function targetGroup() {
  return selectedGroup() ?? (state.selection.kind === 'receiver' ? null : state.groups[0] ?? null);
}

export function groupLocal(id) {
  let local = state.groupLocal.get(id);
  if (!local) {
    local = { receivedAt: Date.now(), startingSince: null, optimistic: null, sleepAt: null };
    state.groupLocal.set(id, local);
  }

  return local;
}

export function receiverLocal(id) {
  let local = state.receiverLocal.get(id);
  if (!local) {
    local = { buffering: false, lastStation: null };
    state.receiverLocal.set(id, local);
  }

  return local;
}

export function isGroupPlaying(group) {
  const local = groupLocal(group.id);
  if (local.optimistic && Date.now() - local.optimistic.at < OPTIMISTIC_MS) {
    return local.optimistic.playing;
  }

  return isPlayingTransport(group.playback?.transport);
}

export function isGroupBuffering(group) {
  const local = groupLocal(group.id);
  const transport = group.playback?.transport;
  if (local.startingSince && (transport === 'playing' || Date.now() - local.startingSince > STARTING_TIMEOUT_MS)) {
    local.startingSince = null;
  }

  return transport === 'transitioning' || local.startingSince !== null;
}

export function markStarting(groupId) {
  groupLocal(groupId).startingSince = Date.now();
}

export function cancelStarting(groupId) {
  groupLocal(groupId).startingSince = null;
}

export function groupVolume(group) {
  return isHolding(`group-volume:${group.id}`) ? groupLocal(group.id).volume ?? group.volume : group.volume;
}

export function memberVolume(member) {
  const key = `player-volume:${member.id}`;
  return isHolding(key) ? state.memberLocal.get(member.id) ?? member.volume : member.volume;
}

export function groupPosition(group) {
  const playback = group.playback;
  if (!playback || typeof playback.positionSeconds !== 'number') {
    return null;
  }

  const local = groupLocal(group.id);
  if (isHolding(`seek:${group.id}`) && typeof local.seek === 'number') {
    return local.seek;
  }

  const elapsed = playback.transport === 'playing' ? (Date.now() - local.receivedAt) / 1000 : 0;
  const position = playback.positionSeconds + elapsed;
  return typeof playback.durationSeconds === 'number' && playback.durationSeconds > 0
    ? Math.min(position, playback.durationSeconds)
    : position;
}

export function sleepRemaining(group) {
  const local = groupLocal(group.id);
  const base = local.sleepOverride !== undefined ? local.sleepOverride : group.playback?.sleepTimerSeconds;
  if (typeof base !== 'number' || base <= 0) {
    return null;
  }

  const since = local.sleepOverride !== undefined ? local.sleepAt : local.receivedAt;
  const remaining = base - (Date.now() - since) / 1000;
  return remaining > 0 ? remaining : null;
}

export function receiverVolume(receiver) {
  return isHolding(`receiver-volume:${receiver.id}`) ? receiverLocal(receiver.id).volume ?? receiver.volume : receiver.volume;
}
