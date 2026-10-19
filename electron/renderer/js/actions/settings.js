import { api } from '../api.js';
import { t } from '../i18n.js';
import { state, invalidate } from '../state.js';
import { notify, showError } from '../ui.js';
import { applySettings } from './core.js';

const HOTKEY_NAMES = ['playPause', 'next', 'previous', 'volumeUp', 'volumeDown'];

export function resetSettingsDraft() {
  const s = state.settings;
  if (!s) {
    state.settingsDraft = null;
    return;
  }

  state.settingsDraft = {
    language: s.language ?? '',
    theme: s.theme ?? 'system',
    startWithWindows: Boolean(s.startWithWindows),
    startMinimized: Boolean(s.startMinimized),
    closeToTray: s.closeToTray !== false,
    checkForUpdates: s.checkForUpdates !== false,
    windowsMediaControls: s.windowsMediaControls !== false,
    manualAddresses: (s.manualPlayerAddresses ?? []).join('\n'),
    volumeStep: s.volumeStep ?? 5,
    volumeFadeSeconds: (s.volumeFadeMilliseconds ?? 2000) / 1000,
    hotkeysEnabled: s.hotkeys?.enabled !== false,
    hotkeys: Object.fromEntries(HOTKEY_NAMES.map((name) => [name, s.hotkeys?.[name] ?? ''])),
  };
}

export function updateDraft(patch) {
  if (state.settingsDraft) {
    Object.assign(state.settingsDraft, patch);
    invalidate();
  }
}

export function setDraftHotkey(name, value) {
  if (state.settingsDraft) {
    state.settingsDraft.hotkeys[name] = value;
    invalidate();
  }
}

export function discardSettings() {
  resetSettingsDraft();
  notify(t('settings.discarded'), 'info');
  invalidate();
}

// Writes the full settings object: the latest server copy with the fields of this page applied.
export async function saveSettings() {
  const draft = state.settingsDraft;
  if (!draft || !state.settings) {
    return;
  }

  const hotkeys = { ...(state.settings.hotkeys ?? {}), enabled: draft.hotkeysEnabled };
  for (const name of HOTKEY_NAMES) {
    hotkeys[name] = draft.hotkeys[name] ? draft.hotkeys[name] : null;
  }

  const settings = {
    ...state.settings,
    language: draft.language || null,
    theme: draft.theme,
    startWithWindows: draft.startWithWindows,
    startMinimized: draft.startMinimized,
    closeToTray: draft.closeToTray,
    checkForUpdates: draft.checkForUpdates,
    windowsMediaControls: draft.windowsMediaControls,
    manualPlayerAddresses: draft.manualAddresses.split(/[\r\n,;]+/).map((a) => a.trim()).filter(Boolean),
    volumeStep: Math.round(draft.volumeStep),
    volumeFadeMilliseconds: Math.round(draft.volumeFadeSeconds * 1000),
    hotkeys,
  };

  try {
    const saved = await api.put('/api/settings', settings);
    await applySettings(saved ?? settings);
    resetSettingsDraft();
    notify(t('settings.saved'), 'success');
  } catch (error) {
    showError(error);
  }

  invalidate();
}

// ---- Updates ----

export async function checkUpdates({ manual = false } = {}) {
  state.update.checking = true;
  if (manual) {
    state.update.status = t('update.checking');
  }

  invalidate();
  try {
    const result = await api.get('/api/update');
    const available = result?.available ?? null;
    state.update.current = result?.current ?? state.version;
    if (available) {
      state.update.info = available;
      state.update.status = t('update.available', available.version, state.update.current);
      if (manual) {
        state.update.dismissed = false;
      }
    } else {
      state.update.info = null;
      state.update.status = t('update.up_to_date', state.update.current);
    }
  } catch (error) {
    state.update.status = manual ? error?.message || t('electron.connection.failed') : null;
  } finally {
    state.update.checking = false;
    invalidate();
  }
}

export function updateBannerVisible() {
  const info = state.update.info;
  return Boolean(info) && !state.update.dismissed && info.version !== state.settings?.skippedUpdateVersion;
}

export function dismissUpdate() {
  state.update.dismissed = true;
  invalidate();
}

export async function skipUpdate() {
  const version = state.update.info?.version;
  if (!version || !state.settings) {
    return;
  }

  state.update.dismissed = true;
  try {
    const saved = await api.put('/api/settings', { ...state.settings, skippedUpdateVersion: version });
    await applySettings(saved);
    notify(t('update.skipped', version), 'info');
  } catch (error) {
    showError(error);
  }
}

export function openRelease() {
  const url = state.update.info?.url;
  if (url) {
    window.sonosdesk.openExternal(url);
  }
}
