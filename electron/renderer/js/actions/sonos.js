import { api, enc } from '../api.js';
import { debouncer } from '../debounce.js';
import { t } from '../i18n.js';
import { clamp, nextRepeat } from '../format.js';
import {
  state, invalidate, groupLocal, groupVolume, isGroupPlaying, markStarting, cancelStarting, selectedGroup, targetGroup,
} from '../state.js';
import { attempt, notify, showError, confirmDialog, openDialog, updateDialog } from '../ui.js';

const groupPath = (group, action) => `/api/groups/${enc(group.id)}/${action}`;
const playerPath = (id, action) => `/api/players/${enc(id)}/${action}`;

async function groupCommand(group, action, body, { playing } = {}) {
  const local = groupLocal(group.id);
  if (playing !== undefined) {
    local.optimistic = { playing, at: Date.now() };
    if (playing) {
      markStarting(group.id);
    } else {
      cancelStarting(group.id);
    }

    invalidate();
  }

  try {
    await api.post(groupPath(group, action), body);
    return true;
  } catch (error) {
    local.optimistic = null;
    cancelStarting(group.id);
    showError(error);
    return false;
  } finally {
    invalidate();
  }
}

export const togglePlay = (group) => groupCommand(group, 'toggle', undefined, { playing: !isGroupPlaying(group) });
export const nextTrack = (group) => groupCommand(group, 'next');
export const previousTrack = (group) => groupCommand(group, 'previous');

export function toggleShuffle(group) {
  const playback = group.playback;
  return groupCommand(group, 'playmode', { shuffle: !playback?.shuffle, repeat: playback?.repeat ?? 'off' });
}

export function cycleRepeat(group) {
  const playback = group.playback;
  return groupCommand(group, 'playmode', { shuffle: Boolean(playback?.shuffle), repeat: nextRepeat(playback?.repeat ?? 'off') });
}

export async function toggleCrossfade(group) {
  const enabled = group.playback?.crossfade !== true;
  if (group.playback) {
    group.playback.crossfade = enabled;
  }

  await groupCommand(group, 'crossfade', { enabled });
}

export async function setSleepTimer(group, minutes) {
  const ok = await groupCommand(group, 'sleep', { minutes });
  if (ok) {
    const local = groupLocal(group.id);
    local.sleepOverride = minutes > 0 ? minutes * 60 : null;
    local.sleepAt = Date.now();
    notify(minutes > 0 ? t('group.sleep_set', group.name, minutes) : t('group.sleep_cancelled', group.name), 'success');
  }
}

export function seek(group, seconds) {
  const local = groupLocal(group.id);
  local.seek = seconds;
  invalidate();
  debouncer(`seek:${group.id}`, async (value) => {
    await api.post(groupPath(group, 'seek'), { seconds: value });
    const current = groupLocal(group.id);
    if (group.playback) {
      group.playback.positionSeconds = value;
    }

    current.receivedAt = Date.now();
  }).submit(Math.round(seconds), (error) => showError(error, 'group.seek_failed'));
}

// Single rooms are set per player (like WPF), groups through the group volume.
export function setGroupVolume(group, volume) {
  const value = clamp(Math.round(volume), 0, 100);
  groupLocal(group.id).volume = value;
  invalidate();
  debouncer(`group-volume:${group.id}`, (v) => (group.isGroup
    ? api.post(groupPath(group, 'volume'), { volume: v })
    : api.post(playerPath(group.coordinatorId, 'volume'), { volume: v }))).submit(value, (error) => showError(error, 'group.volume_failed'));
}

export async function toggleGroupMute(group) {
  const muted = !group.muted;
  group.muted = muted;
  invalidate();
  try {
    await (group.isGroup
      ? api.post(groupPath(group, 'mute'), { muted })
      : api.post(playerPath(group.coordinatorId, 'mute'), { muted }));
  } catch (error) {
    group.muted = !muted;
    showError(error);
    invalidate();
  }
}

export function adjustGroupVolume(group, direction) {
  const step = state.settings?.volumeStep ?? 5;
  setGroupVolume(group, groupVolume(group) + direction * step);
}

export function setMemberVolume(member, volume) {
  const value = clamp(Math.round(volume), 0, 100);
  state.memberLocal.set(member.id, value);
  invalidate();
  debouncer(`player-volume:${member.id}`, (v) => api.post(playerPath(member.id, 'volume'), { volume: v }))
    .submit(value, (error) => showError(error, 'group.member_volume_failed'));
}

export async function toggleMemberMute(member) {
  const muted = !member.muted;
  member.muted = muted;
  invalidate();
  try {
    await api.post(playerPath(member.id, 'mute'), { muted });
  } catch (error) {
    member.muted = !muted;
    showError(error);
    invalidate();
  }
}

// ---- Favorites ----

export async function loadFavorites() {
  if (!state.capabilities.has('favorites') || state.favorites.loading) {
    return;
  }

  state.favorites.loading = true;
  invalidate();
  try {
    const items = await api.get('/api/favorites');
    state.favorites.items = Array.isArray(items) ? items : [];
    state.favorites.loaded = true;
  } catch (error) {
    showError(error);
  } finally {
    state.favorites.loading = false;
    invalidate();
  }
}

export async function playFavorite(favorite) {
  const group = targetGroup();
  if (!group) {
    notify(t('main.select_room_first'), 'warning');
    return;
  }

  if (await groupCommand(group, 'favorite', { favoriteId: favorite.id }, { playing: true })) {
    setTimeout(loadQueue, 1500);
  }
}

// ---- Queue ----

export async function loadQueue() {
  const group = selectedGroup();
  if (!group || !state.capabilities.has('queue')) {
    return;
  }

  const request = ++state.queue.request;
  state.queue.loading = true;
  invalidate();
  try {
    const page = await api.get(`${groupPath(group, 'queue')}?start=0&count=100`);
    if (request !== state.queue.request) {
      return;
    }

    state.queue.groupId = group.id;
    state.queue.items = Array.isArray(page?.items) ? page.items : [];
    state.queue.total = typeof page?.total === 'number' ? page.total : state.queue.items.length;
  } catch {
    // The queue is optional information; a failed load leaves the card empty.
  } finally {
    if (request === state.queue.request) {
      state.queue.loading = false;
      invalidate();
    }
  }
}

export async function playQueueItem(item) {
  const group = selectedGroup();
  if (group) {
    await groupCommand(group, 'queue/play', { position: item.position }, { playing: true });
  }
}

export async function removeQueueItem(item) {
  const group = selectedGroup();
  if (group && await attempt(() => api.del(`${groupPath(group, 'queue')}/${item.position}`))) {
    loadQueue();
  }
}

export async function clearQueue() {
  const group = selectedGroup();
  if (!group || state.queue.items.length === 0 || !(await confirmDialog(t('queue.clear_confirm', group.name), t('np.queue_clear')))) {
    return;
  }

  if (await attempt(() => api.del(groupPath(group, 'queue')), t('queue.cleared'))) {
    loadQueue();
  }
}

// ---- Sound ----

export function resetSoundTarget() {
  const group = selectedGroup();
  const coordinator = group?.members?.find((m) => m.isCoordinator) ?? group?.members?.[0] ?? null;
  state.sound = { ...state.sound, playerId: coordinator?.id ?? null, data: null, loading: false, request: state.sound.request + 1 };
  if (state.sound.expanded) {
    loadSound();
  }
}

export function toggleSoundExpanded() {
  state.sound.expanded = !state.sound.expanded;
  if (state.sound.expanded) {
    if (!state.sound.playerId) {
      resetSoundTarget();
    }

    loadSound();
  }

  invalidate();
}

export function setSoundRoom(playerId) {
  state.sound.playerId = playerId;
  state.sound.data = null;
  loadSound();
}

function roomName(playerId) {
  return state.rooms.find((r) => r.id === playerId)?.name
    ?? state.groups.flatMap((g) => g.members ?? []).find((m) => m.id === playerId)?.room
    ?? '';
}

export async function loadSound() {
  const playerId = state.sound.playerId;
  if (!playerId || !state.capabilities.has('soundSettings')) {
    return;
  }

  const request = ++state.sound.request;
  state.sound.loading = true;
  invalidate();
  try {
    const data = await api.get(playerPath(playerId, 'sound'));
    if (request === state.sound.request) {
      state.sound.data = data;
    }
  } catch {
    if (request === state.sound.request) {
      notify(t('sound.load_failed', roomName(playerId)), 'warning');
    }
  } finally {
    if (request === state.sound.request) {
      state.sound.loading = false;
      invalidate();
    }
  }
}

export function setSound(setting, value) {
  const playerId = state.sound.playerId;
  const data = state.sound.data;
  if (!playerId || !data) {
    return;
  }

  data[setting] = value;
  invalidate();
  const numeric = typeof value === 'boolean' ? (value ? 1 : 0) : value;
  const send = (v) => api.post(playerPath(playerId, 'sound'), { setting, value: v });
  const onError = (error) => notify(error?.message || t('sound.save_failed', roomName(playerId)), 'error');
  if (setting === 'bass' || setting === 'treble') {
    debouncer(`sound:${playerId}:${setting}`, send).submit(numeric, onError);
  } else {
    send(numeric).catch(onError);
  }
}

export function resetEqualizer() {
  setSound('bass', 0);
  setSound('treble', 0);
}

// ---- Grouping ----

export async function toggleRoomMembership(room, group) {
  const isMember = group.members?.some((m) => m.id === room.id);
  state.busyChips.add(room.id);
  invalidate();
  await attempt(() => (isMember
    ? api.post(playerPath(room.id, 'leave'))
    : api.post(playerPath(room.id, 'join'), { groupId: group.id })));
  state.busyChips.delete(room.id);
  invalidate();
}

export async function toggleReceiverLink(receiver, group) {
  const linked = isReceiverLinkedTo(receiver, group);
  const key = `receiver:${receiver.id}`;
  state.busyChips.add(key);
  invalidate();
  if (await attempt(() => api.post(`/api/receivers/${enc(receiver.id)}/link`, { room: linked ? null : group.coordinatorRoom }))) {
    receiver.linkedRoom = linked ? null : group.coordinatorRoom;
  }

  state.busyChips.delete(key);
  invalidate();
}

export function isReceiverLinkedTo(receiver, group) {
  return Boolean(receiver.linkedRoom) && (group.coordinatorRoom === receiver.linkedRoom || group.members?.some((m) => m.room === receiver.linkedRoom));
}

export async function partyMode(group) {
  try {
    const result = await api.post('/api/party', group ? { coordinatorId: group.coordinatorId } : {});
    notify(result?.message || t('party.started', state.rooms.filter((r) => r.controllable).length), 'success');
  } catch (error) {
    showError(error);
  }
}

export function dissolveGroup(group) {
  return attempt(() => api.post(groupPath(group, 'dissolve')), t('main.group_dissolved'));
}

// ---- Discovery and global actions ----

export async function rescan() {
  const previous = state.discovery;
  state.discovery = { ...(previous ?? {}), phase: 'searching' };
  invalidate();
  try {
    const discovery = await api.post('/api/discovery/rescan');
    state.discovery = discovery ?? previous;
    if (discovery?.census) {
      notify(t('main.devices_found_census', discovery.online, discovery.census.mediaRenderers, discovery.census.otherUpnp), 'success');
    } else if (discovery?.online > 0) {
      notify(t('main.devices_found', discovery.online), 'success');
    }
  } catch (error) {
    state.discovery = previous;
    showError(error);
  } finally {
    invalidate();
  }
}

export async function addSpeaker(address) {
  const host = address.trim();
  notify(t('main.checking_host', host), 'info');
  try {
    const result = await api.post('/api/speakers', { address: host });
    notify(result?.message || t('main.speaker_not_found', host), result?.found ? 'success' : 'warning');
  } catch (error) {
    showError(error);
  }
}

export async function pauseAll() {
  try {
    const result = await api.post('/api/pause-all');
    const paused = typeof result?.paused === 'number' ? result.paused : 0;
    notify(paused === 0 ? t('main.nothing_was_playing') : t('main.groups_paused', paused), 'success');
  } catch (error) {
    showError(error);
  }
}

export async function showSpeakerInfo(playerId, roomNameText, model) {
  openDialog({ type: 'info', title: t('info.title', roomNameText), room: roomNameText, model, rows: null, kind: 'speaker' });
  try {
    const rows = await api.get(playerPath(playerId, 'info'));
    updateDialog({ rows: Array.isArray(rows) ? rows : [] });
  } catch (error) {
    updateDialog({ rows: [], error: error?.message || t('info.unknown_speaker') });
  }
}
