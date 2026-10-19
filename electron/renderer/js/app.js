import { html, render } from '../vendor/lit-html/lit-html.js';
import { api, connectEvents } from './api.js';
import { loadLanguage, t } from './i18n.js';
import { receiverDisplay } from './format.js';
import {
  state, invalidate, setRenderer, isGroupBuffering, isGroupPlaying, receiverLocal, selectedReceiver, targetGroup,
} from './state.js';
import { closeDialog, closeMenu, notify } from './ui.js';
import { applyGroup, applyReceiver, applySettings, applySnapshot, refreshStations, selectGroup, selectReceiver, showPage } from './actions/core.js';
import { adjustGroupVolume, nextTrack, pauseAll, previousTrack, rescan, toggleGroupMute, togglePlay } from './actions/sonos.js';
import { adjustReceiverVolume, toggleReceiverMute, toggleReceiverPlay } from './actions/receivers.js';
import { playSavedStationById } from './actions/radio.js';
import { checkUpdates } from './actions/settings.js';
import { sidebarView } from './views/sidebar.js';
import { nowPlayingView } from './views/nowplaying.js';
import { receiverView } from './views/receiver.js';
import { radioView } from './views/radio.js';
import { settingsView } from './views/settings.js';
import { barsView, dialogView, menuView } from './views/overlays.js';
import { icons } from './views/icons.js';

const root = document.getElementById('app');
const overlay = document.getElementById('overlay');
let lastTrayState = '';
let lastDialog = null;
let lastMenu = null;

function pageView() {
  switch (state.page) {
    case 'receiver':
      return selectedReceiver() ? receiverView() : nowPlayingView();
    case 'radio':
      return radioView();
    case 'settings':
      return settingsView();
    default:
      return nowPlayingView();
  }
}

function bootErrorView() {
  return html`<div class="boot-error">
    <span class="empty-icon">${icons.warning}</span>
    <p>${state.bootError}</p>
    <button type="button" class="btn accent" @click=${boot}>${t('electron.startup.retry')}</button>
  </div>`;
}

function appView() {
  if (state.bootError) {
    return bootErrorView();
  }

  if (!state.booted) {
    return html`<div class="boot-loading"><span class="spinner large"></span></div>`;
  }

  return html`<div class="shell">
    ${sidebarView()}
    <main class="content" aria-live="off">
      ${barsView()}
      ${pageView()}
    </main>
  </div>`;
}

function placeOverlays() {
  const menu = overlay.querySelector('.menu');
  if (menu && state.menu !== lastMenu) {
    const rect = menu.getBoundingClientRect();
    const x = Math.max(4, Math.min(state.menu.x, window.innerWidth - rect.width - 4));
    const y = state.menu.y + rect.height > window.innerHeight - 4 ? Math.max(4, state.menu.y - rect.height) : state.menu.y;
    menu.style.left = `${x}px`;
    menu.style.top = `${y}px`;
    menu.querySelector('[role="menuitem"]:not([disabled])')?.focus();
  }

  lastMenu = state.menu;
  if (state.dialog && state.dialog !== lastDialog) {
    const dialog = overlay.querySelector('.dialog');
    (dialog?.querySelector('[data-autofocus]') ?? dialog?.querySelector('input, select, button'))?.focus();
  }

  lastDialog = state.dialog;
}

function renderAll() {
  render(appView(), root);
  render(html`${dialogView()}${menuView()}`, overlay);
  placeOverlays();
  syncTray();
}

// ---- Tray ----

function trayState() {
  const receiver = selectedReceiver();
  const group = receiver ? null : targetGroup();
  const rooms = [
    ...state.groups.map((g) => ({ kind: 'group', id: g.id, name: g.name, active: !receiver && group?.id === g.id })),
    ...state.receivers.map((r) => ({ kind: 'receiver', id: r.id, name: r.name, active: receiver?.id === r.id })),
  ];
  const base = {
    rooms,
    stations: state.stations.map((s) => ({ id: s.id, name: s.name })),
    volumeStep: state.settings?.volumeStep ?? 5,
  };
  if (receiver) {
    const display = receiverDisplay(receiver, { buffering: receiverLocal(receiver.id).buffering }, t);
    return {
      ...base,
      target: { kind: 'receiver', id: receiver.id, name: receiver.name },
      title: [display.title, display.artist].filter(Boolean).join(' – ') || null,
      playing: receiver.playing,
      buffering: receiverLocal(receiver.id).buffering,
      muted: receiver.muted,
      canNext: false,
      canPrevious: false,
      canPlayStations: true,
    };
  }

  if (!group) {
    return { ...base, target: null };
  }

  const playback = group.playback;
  return {
    ...base,
    target: { kind: 'group', id: group.id, name: group.name },
    title: [playback?.displayTitle, playback?.displayArtist].filter(Boolean).join(' – ') || null,
    playing: isGroupPlaying(group),
    buffering: isGroupBuffering(group),
    muted: group.muted,
    canNext: playback ? playback.canSkipNext !== false : true,
    canPrevious: playback ? playback.canSkipPrevious !== false : true,
    canPlayStations: state.capabilities.has('playStreamUrl'),
  };
}

function syncTray() {
  if (!state.booted) {
    return;
  }

  const next = trayState();
  const json = JSON.stringify(next);
  if (json !== lastTrayState) {
    lastTrayState = json;
    window.sonosdesk.setTrayState(next);
  }
}

// ---- Commands from tray, global hotkeys and window shortcuts ----

function noTarget() {
  notify(t('main.no_room_available'), 'warning');
}

function runCommand(command) {
  if (!command || typeof command.type !== 'string') {
    return;
  }

  const receiver = selectedReceiver();
  const group = receiver ? null : targetGroup();
  switch (command.type) {
    case 'toggle':
      if (receiver) {
        toggleReceiverPlay(receiver);
      } else if (group) {
        togglePlay(group);
      } else {
        noTarget();
      }

      break;
    case 'next':
      if (group) {
        nextTrack(group);
      } else if (!receiver) {
        noTarget();
      }

      break;
    case 'previous':
      if (group) {
        previousTrack(group);
      } else if (!receiver) {
        noTarget();
      }

      break;
    case 'volume': {
      const direction = command.direction > 0 ? 1 : -1;
      if (receiver) {
        adjustReceiverVolume(receiver, direction);
      } else if (group) {
        adjustGroupVolume(group, direction);
      } else {
        noTarget();
      }

      break;
    }

    case 'mute':
      if (receiver) {
        toggleReceiverMute(receiver);
      } else if (group) {
        toggleGroupMute(group);
      }

      break;
    case 'select':
      if (command.kind === 'receiver') {
        selectReceiver(command.id, { openPage: false });
      } else {
        selectGroup(command.id, { openPage: false });
      }

      break;
    case 'playStation':
      playSavedStationById(command.id);
      break;
    case 'pauseAll':
      pauseAll();
      break;
    case 'open':
      if (command.page === 'settings') {
        showPage('settings');
      }

      break;
    case 'hotkeysFailed':
      if (Array.isArray(command.keys) && command.keys.length > 0) {
        notify(t('app.hotkeys_in_use', command.keys.join(', ')), 'warning');
      }

      break;
    default:
      break;
  }
}

function isTextField(element) {
  return element instanceof HTMLTextAreaElement
    || element instanceof HTMLSelectElement
    || (element instanceof HTMLInputElement && !['range', 'checkbox', 'button'].includes(element.type));
}

function onKeyDown(event) {
  if (event.key === 'Escape') {
    if (state.menu) {
      closeMenu();
      return;
    }

    if (state.dialog) {
      closeDialog();
      return;
    }
  }

  if (state.dialog || state.menu || !state.booted) {
    return;
  }

  if (event.key === 'F5') {
    event.preventDefault();
    rescan();
    return;
  }

  const mod = event.ctrlKey || (state.app.platform === 'darwin' && event.metaKey);
  if (!mod || event.altKey) {
    return;
  }

  const text = isTextField(event.target);
  const keyActions = {
    ',': () => showPage('settings'),
    ' ': () => runCommand({ type: 'toggle' }),
    ArrowRight: () => runCommand({ type: 'next' }),
    ArrowLeft: () => runCommand({ type: 'previous' }),
    ArrowUp: () => runCommand({ type: 'volume', direction: 1 }),
    ArrowDown: () => runCommand({ type: 'volume', direction: -1 }),
  };
  if (event.key.toLowerCase() === 'm' && event.ctrlKey && !text) {
    event.preventDefault();
    runCommand({ type: 'mute' });
    return;
  }

  const action = keyActions[event.key];
  if (action && (event.key === ',' || !text)) {
    event.preventDefault();
    action();
  }
}

// ---- Events ----

function onEvent(message) {
  switch (message.type) {
    case 'snapshot':
      applySnapshot(message.data);
      break;
    case 'group':
      applyGroup(message.data);
      break;
    case 'receiver':
      applyReceiver(message.data);
      break;
    case 'discovery':
      state.discovery = message.data;
      invalidate();
      break;
    case 'settings':
      applySettings(message.data).then(refreshStations);
      break;
    case 'notice':
      notify(message.data?.message, ['info', 'success', 'warning', 'error'].includes(message.data?.severity) ? message.data.severity : 'info');
      break;
    default:
      break;
  }
}

let events = null;

async function boot() {
  state.bootError = null;
  invalidate();
  try {
    const [server, app] = await Promise.all([window.sonosdesk.getServer(), window.sonosdesk.getAppInfo()]);
    if (!server) {
      throw new Error('server');
    }

    state.server = server;
    state.app = app ?? state.app;
    document.body.classList.add(`platform-${state.app.platform}`);
    await loadLanguage(null);
    const snapshot = await api.get('/api/snapshot');
    await applySnapshot(snapshot);
    await refreshStations();
    state.booted = true;
    invalidate();
  } catch {
    await loadLanguage(null);
    state.bootError = t('electron.connection.failed');
    invalidate();
    return;
  }

  events?.close();
  events = connectEvents(state.server, {
    onOpen: () => {
      state.connected = true;
      invalidate();
    },
    onClose: () => {
      state.connected = false;
      invalidate();
    },
    onMessage: onEvent,
  });

  if (state.settings?.checkForUpdates !== false) {
    checkUpdates();
  }
}

setRenderer(renderAll);
window.sonosdesk.onTrayCommand(runCommand);
document.addEventListener('keydown', onKeyDown);
window.addEventListener('blur', closeMenu);
window.addEventListener('resize', closeMenu);
document.addEventListener('scroll', closeMenu, true);

// Positions, countdowns and buffering timeouts advance once per second.
setInterval(() => {
  if (state.booted && (state.page === 'nowplaying' || state.page === 'receiver' || state.groups.some((g) => isGroupPlaying(g) || isGroupBuffering(g)))) {
    invalidate();
  }
}, 1000);

boot();
