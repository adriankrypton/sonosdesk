import { api, enc } from '../api.js';
import { debouncer } from '../debounce.js';
import { t } from '../i18n.js';
import { clamp } from '../format.js';
import { state, invalidate, receiverLocal, receiverVolume } from '../state.js';
import { attempt, confirmDialog, notify, openDialog, showError, updateDialog } from '../ui.js';
import { applyReceiver, selectReceiver, showPage } from './core.js';

const path = (receiver, action) => `/api/receivers/${enc(receiver.id)}${action ? `/${action}` : ''}`;

async function refreshReceiver(receiver) {
  try {
    const list = await api.get('/api/receivers');
    const fresh = Array.isArray(list) ? list.find((r) => r.id === receiver.id) : null;
    if (fresh) {
      applyReceiver(fresh);
    }
  } catch {
    // The next receiver event brings the state.
  }
}

export async function togglePower(receiver) {
  if (await attempt(() => api.post(path(receiver, 'power'), { on: !receiver.on }))) {
    refreshReceiver(receiver);
  }
}

export function setReceiverVolume(receiver, volume) {
  const value = clamp(Math.round(volume), 0, 100);
  receiverLocal(receiver.id).volume = value;
  invalidate();
  debouncer(`receiver-volume:${receiver.id}`, (v) => api.post(path(receiver, 'volume'), { volume: v }))
    .submit(value, (error) => showError(error, 'receiver.volume_failed'));
}

export function adjustReceiverVolume(receiver, direction) {
  setReceiverVolume(receiver, receiverVolume(receiver) + direction * (state.settings?.volumeStep ?? 5));
}

export async function toggleReceiverMute(receiver) {
  const muted = !receiver.muted;
  receiver.muted = muted;
  invalidate();
  try {
    await api.post(path(receiver, 'mute'), { muted });
  } catch (error) {
    receiver.muted = !muted;
    showError(error);
    invalidate();
  }
}

export async function playOnReceiver(receiver, station) {
  const local = receiverLocal(receiver.id);
  local.buffering = true;
  local.lastStation = station;
  invalidate();
  try {
    await api.post(path(receiver, 'play'), { url: station.streamUrl, name: station.name });
  } catch (error) {
    showError(error);
  } finally {
    local.buffering = false;
    invalidate();
    refreshReceiver(receiver);
  }
}

// Play/pause like Sonos: a live stream stops; play resumes the last station or the first saved one.
export function toggleReceiverPlay(receiver) {
  if (receiver.playing) {
    return attempt(() => api.post(path(receiver, 'stop'))).then(() => refreshReceiver(receiver));
  }

  const station = receiverLocal(receiver.id).lastStation ?? state.stations[0];
  return station ? playOnReceiver(receiver, station) : Promise.resolve();
}

export function canPlayReceiver(receiver) {
  return receiver.playing || Boolean(receiverLocal(receiver.id).lastStation) || state.stations.length > 0;
}

export async function linkReceiver(receiver, room) {
  receiver.linkedRoom = room;
  receiver.syncNotice = null;
  invalidate();
  await attempt(() => api.post(path(receiver, 'link'), { room }));
}

export async function removeReceiver(receiver) {
  if (!(await confirmDialog(t('receiver.remove_confirm', receiver.name), t('receiver.remove')))) {
    return;
  }

  if (await attempt(() => api.del(path(receiver)))) {
    state.receivers = state.receivers.filter((r) => r.id !== receiver.id);
    if (state.selection.kind === 'receiver' && state.selection.id === receiver.id) {
      state.selection = { kind: null, id: null, room: null };
      showPage('nowplaying');
    }

    invalidate();
  }
}

export async function addReceiver(host) {
  try {
    const receiver = await api.post('/api/receivers', { host: host.trim() });
    if (receiver?.id) {
      applyReceiver(receiver);
      notify(t('receiver.added', receiver.name), 'success');
      selectReceiver(receiver.id);
    }

    return true;
  } catch (error) {
    showError(error);
    return false;
  }
}

export async function searchReceivers() {
  updateDialog({ searching: true, found: [], message: null });
  try {
    const found = await api.get('/api/receivers/search');
    const list = Array.isArray(found) ? found : [];
    updateDialog({ searching: false, found: list, message: list.length > 0 ? t('dlg.receiver.pick') : t('dlg.receiver.none_found') });
  } catch (error) {
    updateDialog({ searching: false, found: [], message: error?.message || t('dlg.receiver.none_found') });
  }
}

export async function showReceiverInfo(receiver) {
  openDialog({ type: 'info', title: t('info.title', receiver.name), room: receiver.name, model: receiver.model, rows: null, kind: 'receiver' });
  try {
    const rows = await api.get(path(receiver, 'info'));
    updateDialog({ rows: Array.isArray(rows) ? rows : [] });
  } catch (error) {
    updateDialog({ rows: [], error: error?.message || t('receiver.unreachable', receiver.name) });
  }
}

// ---- Sound ----

export function toggleReceiverSound(receiver) {
  const sound = state.receiverSound;
  if (sound.receiverId !== receiver.id) {
    state.receiverSound = { expanded: false, receiverId: receiver.id, data: null, loading: false };
  }

  state.receiverSound.expanded = !state.receiverSound.expanded;
  if (state.receiverSound.expanded) {
    loadReceiverSound(receiver);
  }

  invalidate();
}

export async function loadReceiverSound(receiver) {
  const sound = state.receiverSound;
  sound.loading = true;
  invalidate();
  try {
    const data = await api.get(path(receiver, 'sound'));
    if (state.receiverSound.receiverId === receiver.id) {
      state.receiverSound.data = data;
    }
  } catch (error) {
    if (state.receiverSound.receiverId === receiver.id) {
      state.receiverSound.data = null;
    }

    notify(error?.message || t('vm.timeout'), 'warning');
  } finally {
    state.receiverSound.loading = false;
    invalidate();
  }
}

export async function setListeningMode(receiver, code) {
  try {
    const sound = await api.post(path(receiver, 'mode'), { code });
    if (sound && state.receiverSound.receiverId === receiver.id) {
      state.receiverSound.data = sound;
    }
  } catch (error) {
    // 409: the receiver refused the mode; the body carries what it really uses.
    if (error?.data?.sound && state.receiverSound.receiverId === receiver.id) {
      state.receiverSound.data = error.data.sound;
    }

    notify(error?.message || t('receiver.mode_unavailable'), 'warning');
  }

  invalidate();
}

export function setTone(receiver, setting, value) {
  const data = state.receiverSound.data;
  if (!data) {
    return;
  }

  data[setting] = value;
  invalidate();
  debouncer(`tone:${receiver.id}`, () => api.post(path(receiver, 'tone'), { bass: data.bass ?? 0, treble: data.treble ?? 0 }))
    .submit(0, (error) => {
      notify(error?.message || t('receiver.tone_failed'), 'warning');
      loadReceiverSound(receiver);
    });
}

export function resetTone(receiver) {
  const data = state.receiverSound.data;
  if (data) {
    data.bass = 0;
    setTone(receiver, 'treble', 0);
  }
}
