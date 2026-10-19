import { api, enc } from '../api.js';
import { t } from '../i18n.js';
import { state, invalidate, markStarting, cancelStarting, groupLocal, selectedReceiver, targetGroup } from '../state.js';
import { attempt, confirmDialog, notify, promptDialog, showError } from '../ui.js';
import { refreshStations } from './core.js';
import { playOnReceiver } from './receivers.js';

export const GENRES = ['pop', 'rock', 'news', 'jazz', 'classical', 'electronic', 'hiphop', 'schlager', '80s', 'chillout'];

const LOGO_SIZE = 256;

export function stationLogo(station) {
  if (station?.logoUrl) {
    return station.logoUrl;
  }

  return station?.faviconUrl && /^https?:\/\//i.test(station.faviconUrl) ? `/api/art?u=${enc(station.faviconUrl)}` : null;
}

export function isSaved(station) {
  return state.stations.some((s) => s.id === station.id);
}

function regionFromLocale() {
  const match = /[-_]([A-Za-z]{2})$/.exec(navigator.language || '');
  return match ? match[1].toUpperCase() : 'DE';
}

// null = all countries.
export function radioCountry() {
  if (state.radio.country === undefined) {
    const saved = state.settings?.radioCountryCode;
    state.radio.country = typeof saved === 'string' && saved ? saved : regionFromLocale();
  }

  return state.radio.country;
}

export function countryName(code) {
  try {
    return new Intl.DisplayNames([document.documentElement.lang || 'en'], { type: 'region' }).of(code) ?? code;
  } catch {
    return code;
  }
}

async function load(url, header) {
  const request = (state.radio.request ?? 0) + 1;
  Object.assign(state.radio, { request, loading: true, emptyKey: null, header });
  invalidate();
  let results = [];
  let ok = true;
  try {
    const data = await api.get(url);
    results = Array.isArray(data) ? data : [];
  } catch {
    ok = false;
  }

  if (request !== state.radio.request) {
    return;
  }

  Object.assign(state.radio, {
    loading: false,
    results,
    emptyKey: !ok ? 'radio.directory_unavailable' : results.length === 0 ? 'radio.no_results' : null,
  });
  invalidate();
}

function countryQuery() {
  const country = radioCountry();
  return country ? `country=${enc(country)}` : '';
}

export function loadPopular() {
  state.radio.query = '';
  const country = radioCountry();
  const query = countryQuery();
  return load(`/api/radio/popular${query ? `?${query}` : ''}`, () => (country
    ? t('radio.popular_country', countryName(country))
    : t('radio.popular_world')));
}

export function loadPopularIfNeeded() {
  if (!state.radio.initialized) {
    state.radio.initialized = true;
    loadPopular();
  }
}

export function search() {
  const text = state.radio.query.trim();
  if (!text) {
    return loadPopular();
  }

  const query = countryQuery();
  return load(`/api/radio/search?q=${enc(text)}${query ? `&${query}` : ''}`, () => t('radio.results_for', text));
}

export function searchGenre(genre) {
  state.radio.query = '';
  const query = countryQuery();
  return load(`/api/radio/search?tag=${enc(genre)}${query ? `&${query}` : ''}`, () => t('radio.genre', genre));
}

export function setCountry(code) {
  state.radio.country = code || null;
  if (state.settings) {
    const settings = { ...state.settings, radioCountryCode: code || null };
    api.put('/api/settings', settings).catch(() => {});
  }

  return state.radio.query.trim() ? search() : loadPopular();
}

export function radioTargetName() {
  const receiver = selectedReceiver();
  return receiver ? receiver.name : targetGroup()?.name ?? null;
}

export function canPlayStreams() {
  return Boolean(selectedReceiver()) || state.capabilities.has('playStreamUrl');
}

export async function playStation(station) {
  const receiver = selectedReceiver();
  if (receiver) {
    await playOnReceiver(receiver, station);
    return;
  }

  const group = targetGroup();
  if (!group) {
    notify(t('radio.select_room_first'), 'warning');
    return;
  }

  markStarting(group.id);
  groupLocal(group.id).optimistic = { playing: true, at: Date.now() };
  invalidate();
  const body = { url: station.streamUrl, title: station.name };
  if (station.faviconUrl && /^https?:\/\//i.test(station.faviconUrl)) {
    body.artUrl = station.faviconUrl;
  }

  try {
    await api.post(`/api/groups/${enc(group.id)}/stream`, body);
  } catch (error) {
    cancelStarting(group.id);
    groupLocal(group.id).optimistic = null;
    showError(error);
  }

  invalidate();
}

export function playSavedStationById(id) {
  const station = state.stations.find((s) => s.id === id);
  return station ? playStation(station) : Promise.resolve();
}

export async function toggleSaved(station) {
  if (isSaved(station)) {
    if (await attempt(() => api.del(`/api/stations/${enc(station.id)}`), t('radio.removed', station.name))) {
      state.stations = state.stations.filter((s) => s.id !== station.id);
      invalidate();
    }

    return;
  }

  const body = { id: station.id, name: station.name, streamUrl: station.streamUrl };
  if (station.faviconUrl) {
    body.faviconUrl = station.faviconUrl;
  }

  if (station.countryCode) {
    body.countryCode = station.countryCode;
  }

  if (await attempt(() => api.post('/api/stations', body), t('radio.added_saved', station.name))) {
    refreshStations();
  }
}

export async function deleteStation(station) {
  if (!(await confirmDialog(t('radio.delete_confirm', station.name), t('radio.menu.delete')))) {
    return;
  }

  if (await attempt(() => api.del(`/api/stations/${enc(station.id)}`), t('radio.removed', station.name))) {
    state.stations = state.stations.filter((s) => s.id !== station.id);
    invalidate();
  }
}

export async function renameStation(station) {
  const value = await promptDialog({ title: t('radio.rename_station_title'), hint: t('radio.rename_station_hint'), value: station.name });
  const name = value?.trim().slice(0, 80);
  if (!name || name === station.name) {
    return;
  }

  if (await attempt(() => api.patch(`/api/stations/${enc(station.id)}`, { name }), t('radio.renamed', name))) {
    refreshStations();
  }
}

export async function addCustomStation(name, url) {
  try {
    const station = await api.post('/api/stations', { name: name.trim(), streamUrl: url.trim() });
    notify(t('radio.custom_added', station?.name ?? name.trim()), 'success');
    refreshStations();
    return true;
  } catch (error) {
    showError(error);
    return false;
  }
}

// Scales the picked image to at most 256 px and uploads it as PNG.
async function toPng(file) {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, LOGO_SIZE / Math.max(bitmap.width, bitmap.height));
  const width = Math.max(1, Math.round(bitmap.width * scale));
  const height = Math.max(1, Math.round(bitmap.height * scale));
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  canvas.getContext('2d').drawImage(bitmap, 0, 0, width, height);
  bitmap.close();
  return new Promise((resolve, reject) => canvas.toBlob((blob) => (blob ? resolve(blob) : reject(new Error('png'))), 'image/png'));
}

export function pickLogo(station) {
  const input = document.createElement('input');
  input.type = 'file';
  input.accept = 'image/png,image/jpeg,image/gif,image/bmp,image/webp';
  input.addEventListener('change', async () => {
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    let blob;
    try {
      blob = await toPng(file);
    } catch {
      notify(t('app.logo_unsupported'), 'warning');
      return;
    }

    if (await attempt(() => api.putBinary(`/api/stations/${enc(station.id)}/logo`, blob, 'image/png'), t('radio.logo_saved', station.name))) {
      refreshStations();
    }
  });
  input.click();
}

export async function resetLogo(station) {
  if (await attempt(() => api.del(`/api/stations/${enc(station.id)}/logo`), t('radio.logo_reset', station.name))) {
    refreshStations();
  }
}
