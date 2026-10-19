import { api } from '../api.js';
import { currentLanguage, loadLanguage } from '../i18n.js';
import { isPlayingTransport } from '../format.js';
import { state, invalidate, groupById, groupLocal, receiverById, receiverLocal } from '../state.js';
import { loadFavorites, loadQueue, resetSoundTarget } from './sonos.js';
import { loadPopularIfNeeded } from './radio.js';
import { resetSettingsDraft } from './settings.js';

function groupWithRoom(room) {
  if (!room) {
    return null;
  }

  return state.groups.find((g) => g.coordinatorRoom === room || g.members?.some((m) => m.room === room)) ?? null;
}

function annotateGroup(dto) {
  const local = groupLocal(dto.id);
  local.receivedAt = Date.now();
  local.sleepOverride = undefined;
  const transport = dto.playback?.transport;
  if (transport === 'playing') {
    local.startingSince = null;
  }

  if (local.optimistic && local.optimistic.playing === isPlayingTransport(transport)) {
    local.optimistic = null;
  }

  return dto;
}

export function applyGroup(dto) {
  if (!dto?.id) {
    return;
  }

  const index = state.groups.findIndex((g) => g.id === dto.id);
  if (index < 0) {
    // Unknown group: the topology changed; a snapshot event follows.
    return;
  }

  state.groups[index] = annotateGroup(dto);
  if (state.selection.kind === 'group' && state.selection.id === dto.id) {
    const position = dto.playback?.source === 'queue' ? dto.playback.queuePosition : null;
    const queue = state.queue;
    if (typeof position === 'number' && queue.groupId === dto.id && !queue.loading && queue.items.length < 100 && position > queue.items.length) {
      loadQueue();
    }
  }

  invalidate();
}

export function applyReceiver(dto) {
  if (!dto?.id) {
    return;
  }

  const index = state.receivers.findIndex((r) => r.id === dto.id);
  if (index < 0) {
    state.receivers.push(dto);
  } else {
    state.receivers[index] = dto;
  }

  rememberReceiverStation(dto);
  invalidate();
}

function rememberReceiverStation(receiver) {
  if (receiver.playing && receiver.stationName) {
    const local = receiverLocal(receiver.id);
    if (!local.lastStation) {
      local.lastStation = state.stations.find((s) => s.name === receiver.stationName) ?? null;
    }
  }
}

export async function applySettings(settings) {
  if (!settings || typeof settings !== 'object') {
    return;
  }

  state.settings = settings;
  await loadLanguage(settings.language);
  window.sonosdesk.settingsChanged(settings, currentLanguage());
  if (state.page === 'settings' && !state.settingsDraft) {
    resetSettingsDraft();
  }

  invalidate();
}

export async function refreshStations() {
  try {
    const stations = await api.get('/api/stations');
    state.stations = Array.isArray(stations) ? stations : [];
    for (const receiver of state.receivers) {
      rememberReceiverStation(receiver);
    }

    invalidate();
  } catch {
    // Keeps the previous list; the next settings event retries.
  }
}

function setSelectedGroup(group) {
  const changed = state.selection.kind !== 'group' || state.selection.id !== group?.id;
  state.selection = group ? { kind: 'group', id: group.id, room: group.coordinatorRoom } : { kind: null, id: null, room: null };
  if (changed) {
    state.queue = { groupId: null, items: [], total: 0, loading: false, request: state.queue.request + 1 };
    resetSoundTarget();
    if (group) {
      loadQueue();
    }
  }
}

function syncSelection(previousActiveRoom) {
  const selection = state.selection;
  if (selection.kind === 'receiver' && receiverById(selection.id)) {
    return;
  }

  let group = selection.kind === 'group' ? groupById(selection.id) : null;
  if (!group && selection.room) {
    group = groupWithRoom(selection.room);
  }

  // Follow the server's active room when something other than this window changed it.
  const activeChanged = state.activeRoom !== previousActiveRoom && state.activeRoom !== state.lastActiveRoomSent;
  if (activeChanged || !group) {
    group = groupWithRoom(state.activeRoom) ?? group ?? state.groups[0] ?? null;
  }

  setSelectedGroup(group);
  if (!group && state.page === 'receiver') {
    state.page = 'nowplaying';
  }
}

export async function applySnapshot(snapshot) {
  if (!snapshot || typeof snapshot !== 'object') {
    return;
  }

  const previousActiveRoom = state.activeRoom;
  state.version = snapshot.version ?? state.version;
  state.backend = snapshot.backend ?? '';
  state.capabilities = new Set(Array.isArray(snapshot.capabilities) ? snapshot.capabilities : []);
  state.discovery = snapshot.discovery ?? null;
  state.groups = (Array.isArray(snapshot.groups) ? snapshot.groups : []).map((g) => annotateGroup(g));
  state.rooms = Array.isArray(snapshot.rooms) ? snapshot.rooms : [];
  state.receivers = Array.isArray(snapshot.receivers) ? snapshot.receivers : [];
  state.activeRoom = snapshot.activeRoom ?? null;
  for (const receiver of state.receivers) {
    rememberReceiverStation(receiver);
  }

  syncSelection(previousActiveRoom);
  if (snapshot.settings) {
    await applySettings(snapshot.settings);
  }

  if (state.groups.length > 0 && state.capabilities.has('favorites') && !state.favorites.loaded && !state.favorites.loading) {
    loadFavorites();
  }

  invalidate();
}

export function showPage(page) {
  state.menu = null;
  if (page === 'settings' && state.page !== 'settings') {
    resetSettingsDraft();
  }

  if (page === 'radio') {
    loadPopularIfNeeded();
  }

  state.page = page;
  invalidate();
  requestAnimationFrame(() => document.querySelector('.page')?.scrollTo?.(0, 0));
}

export function selectGroup(id, { openPage = true } = {}) {
  const group = groupById(id);
  if (!group) {
    return;
  }

  setSelectedGroup(group);
  if (openPage) {
    showPage('nowplaying');
  } else if (state.page === 'receiver') {
    state.page = 'nowplaying';
  }

  state.lastActiveRoomSent = group.coordinatorRoom;
  api.post('/api/active-room', { room: group.coordinatorRoom }).catch(() => {});
  invalidate();
}

export function selectReceiver(id, { openPage = true } = {}) {
  if (!receiverById(id)) {
    return;
  }

  if (state.selection.kind !== 'receiver' || state.selection.id !== id) {
    state.receiverSound = { expanded: false, receiverId: id, data: null, loading: false };
  }

  state.selection = { kind: 'receiver', id, room: null };
  if (openPage || state.page === 'nowplaying') {
    showPage('receiver');
  }

  invalidate();
}
