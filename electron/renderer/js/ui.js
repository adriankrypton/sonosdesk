import { t } from './i18n.js';
import { state, invalidate } from './state.js';

const NOTICE_MS = 6000;
let noticeId = 0;

// InfoBar at the top of the content area; hides itself after 6 s.
export function notify(message, severity = 'info') {
  if (!message) {
    return;
  }

  const id = ++noticeId;
  state.notice = { id, message, severity };
  invalidate();
  setTimeout(() => {
    if (state.notice?.id === id) {
      state.notice = null;
      invalidate();
    }
  }, NOTICE_MS);
}

export function dismissNotice() {
  state.notice = null;
  invalidate();
}

export function errorMessage(error, fallbackKey = 'vm.unexpected_error') {
  if (error?.message) {
    return error.message;
  }

  if (error?.kind === 'unavailable') {
    return t('electron.connection.failed');
  }

  if (error?.kind === 'timeout') {
    return t('vm.timeout');
  }

  return t(fallbackKey);
}

export function showError(error, fallbackKey) {
  const warning = error?.kind === 'notSupported' || error?.status === 409;
  notify(errorMessage(error, fallbackKey), warning ? 'warning' : 'error');
}

// Runs an API call; failures become a notification. Returns true on success.
export async function attempt(action, successMessage) {
  try {
    await action();
    if (successMessage) {
      notify(successMessage, 'success');
    }

    return true;
  } catch (error) {
    showError(error);
    return false;
  }
}

let focusBeforeOverlay = null;

function rememberFocus() {
  // A dialog opened from a menu returns focus to what had it before the menu.
  if (!state.dialog && !state.menu && !document.getElementById('overlay')?.contains(document.activeElement)) {
    focusBeforeOverlay = document.activeElement;
  }
}

function restoreFocus() {
  if (!state.dialog && !state.menu && focusBeforeOverlay?.isConnected) {
    focusBeforeOverlay.focus();
  }
}

export function openDialog(dialog) {
  rememberFocus();
  state.menu = null;
  state.dialog = dialog;
  invalidate();
}

export function closeDialog(result) {
  const dialog = state.dialog;
  state.dialog = null;
  dialog?.resolve?.(result);
  invalidate();
  requestAnimationFrame(restoreFocus);
}

export function updateDialog(patch) {
  if (state.dialog) {
    Object.assign(state.dialog, patch);
    invalidate();
  }
}

export function confirmDialog(message, confirmLabel) {
  return new Promise((resolve) => {
    openDialog({ type: 'confirm', message, confirmLabel, resolve: (value) => resolve(value === true) });
  });
}

export function promptDialog({ title, hint, value = '', okLabel }) {
  return new Promise((resolve) => {
    openDialog({ type: 'prompt', title, hint, value, okLabel, resolve: (result) => resolve(typeof result === 'string' ? result : null) });
  });
}

// Custom context menu: items are { label, action, disabled, checked, header } or 'separator'.
export function openMenu(position, items) {
  rememberFocus();
  state.menu = { x: position.x, y: position.y, items };
  invalidate();
}

export function openMenuAt(element, items) {
  const rect = element.getBoundingClientRect();
  openMenu({ x: rect.left, y: rect.bottom + 4 }, items);
}

export function closeMenu() {
  if (state.menu) {
    state.menu = null;
    invalidate();
    requestAnimationFrame(restoreFocus);
  }
}

export function contextMenuHandler(itemsFactory) {
  return (event) => {
    event.preventDefault();
    event.stopPropagation();
    const items = itemsFactory();
    if (items.length === 0) {
      return;
    }

    // Keyboard-invoked context menus report (0,0); open them at the element instead.
    if (event.clientX === 0 && event.clientY === 0 && event.currentTarget instanceof Element) {
      openMenuAt(event.currentTarget, items);
    } else {
      openMenu({ x: event.clientX, y: event.clientY }, items);
    }
  };
}
