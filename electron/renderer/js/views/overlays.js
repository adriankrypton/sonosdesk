import { html, nothing } from '../../vendor/lit-html/lit-html.js';
import { live } from '../../vendor/lit-html/directives/live.js';
import { t } from '../i18n.js';
import { isLocalIPv4, isSpeakerAddress } from '../format.js';
import { state } from '../state.js';
import { closeDialog, closeMenu, dismissNotice, notify, updateDialog } from '../ui.js';
import { addSpeaker } from '../actions/sonos.js';
import { addReceiver, searchReceivers } from '../actions/receivers.js';
import { addCustomStation } from '../actions/radio.js';
import { dismissUpdate, openRelease, skipUpdate, updateBannerVisible } from '../actions/settings.js';
import { iconButton, spinner } from './common.js';
import { icons, severityIcon } from './icons.js';

// ---- Message bars ----

export function barsView() {
  const notice = state.notice;
  const update = state.update.info;
  return html`<div class="bars">
    ${!state.connected && state.booted ? html`<div class="info-bar warning" role="status">
      <span class="info-icon">${icons.warning}</span><span class="info-text">${t('electron.connection.lost')}</span>
    </div>` : nothing}
    ${notice ? html`<div class="info-bar ${notice.severity}" role=${notice.severity === 'error' ? 'alert' : 'status'}>
      <span class="info-icon">${severityIcon(notice.severity)}</span>
      <span class="info-text">${notice.message}</span>
      ${iconButton({ icon: icons.close, label: t('main.dismiss_message'), className: 'small', onClick: dismissNotice })}
    </div>` : nothing}
    ${updateBannerVisible() ? html`<div class="update-banner" role="status">
      <span class="info-icon accent">${icons.download}</span>
      <div class="update-body">
        <strong>${t('update.available', update.version, state.update.current ?? state.version)}</strong>
        ${update.notes ? html`<p class="update-notes">${update.notes}</p>` : nothing}
        <div class="button-row">
          <button type="button" class="btn accent" @click=${openRelease}>${t('update.download')}</button>
          <button type="button" class="btn" @click=${dismissUpdate}>${t('update.later')}</button>
          <button type="button" class="btn" @click=${skipUpdate}>${t('update.skip')}</button>
        </div>
      </div>
      ${iconButton({ icon: icons.close, label: t('main.dismiss_message'), className: 'small', onClick: dismissUpdate })}
    </div>` : nothing}
  </div>`;
}

// ---- Context menu ----

function onMenuKey(event) {
  const items = [...event.currentTarget.querySelectorAll('[role="menuitem"]:not([disabled])')];
  const index = items.indexOf(document.activeElement);
  if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
    event.preventDefault();
    const next = event.key === 'ArrowDown' ? (index + 1) % items.length : (index - 1 + items.length) % items.length;
    items[next]?.focus();
  } else if (event.key === 'Escape' || event.key === 'Tab') {
    event.preventDefault();
    closeMenu();
  } else if (event.key === 'Home' || event.key === 'End') {
    event.preventDefault();
    items[event.key === 'Home' ? 0 : items.length - 1]?.focus();
  }
}

export function menuView() {
  const menu = state.menu;
  if (!menu) {
    return nothing;
  }

  const run = (item) => () => {
    closeMenu();
    item.action?.();
  };
  return html`<div class="menu-backdrop" @mousedown=${closeMenu} @contextmenu=${(e) => { e.preventDefault(); closeMenu(); }}></div>
    <div class="menu" role="menu" style=${`left:${menu.x}px;top:${menu.y}px`} @keydown=${onMenuKey}>
      ${menu.items.map((item) => (item === 'separator'
        ? html`<div class="menu-separator" role="separator"></div>`
        : item.header
          ? html`<div class="menu-header">${item.label}</div>`
          : html`<button type="button" role="menuitem" class="menu-item" ?disabled=${item.disabled} @click=${run(item)}>
              <span class="menu-icon">${item.icon ?? nothing}</span>
              <span class="menu-label">${item.label}</span>
              ${item.shortcut ? html`<span class="menu-shortcut">${item.shortcut}</span>` : nothing}
            </button>`))}
    </div>`;
}

// ---- Dialogs ----

// Escape closes; Tab stays inside the dialog.
function onDialogKey(event) {
  if (event.key === 'Escape') {
    event.preventDefault();
    closeDialog();
    return;
  }

  if (event.key !== 'Tab') {
    return;
  }

  const focusable = [...event.currentTarget.querySelectorAll('button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled])')];
  if (focusable.length === 0) {
    return;
  }

  const first = focusable[0];
  const last = focusable[focusable.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
}

function dialogFrame(title, body, buttons, { wide = false, onSubmit } = {}) {
  const submit = (event) => {
    event.preventDefault();
    onSubmit?.();
  };
  return html`<div class="dialog-backdrop" @mousedown=${(e) => { if (e.target === e.currentTarget) { closeDialog(); } }}>
    <form class="dialog ${wide ? 'wide' : ''}" role="dialog" aria-modal="true" aria-labelledby="dialog-title" @submit=${submit}
      @keydown=${onDialogKey}>
      <h2 id="dialog-title" class="dialog-title">${title}</h2>
      ${body}
      <div class="dialog-buttons">${buttons}</div>
    </form>
  </div>`;
}

function confirmView(d) {
  return dialogFrame(t('electron.dialog.confirm_title'), html`<p class="dialog-text">${d.message}</p>`, html`
    <button type="button" class="btn" @click=${() => closeDialog(false)}>${t('dlg.prompt.cancel')}</button>
    <button type="submit" class="btn accent" data-autofocus>${d.confirmLabel ?? t('electron.dialog.ok')}</button>`,
  { onSubmit: () => closeDialog(true) });
}

function promptView(d) {
  return dialogFrame(d.title, html`
    ${d.hint ? html`<p class="hint dialog-text">${d.hint}</p>` : nothing}
    <input type="text" class="text-input full" data-autofocus maxlength="80" .value=${live(d.value)} aria-label=${d.title}
      @input=${(e) => { d.value = e.target.value; }}>`, html`
    <button type="button" class="btn" @click=${() => closeDialog(null)}>${t('dlg.prompt.cancel')}</button>
    <button type="submit" class="btn accent">${d.okLabel ?? t('dlg.prompt.ok')}</button>`,
  { onSubmit: () => closeDialog(d.value) });
}

async function copyRows(rows) {
  const text = rows.map((r) => `${r.label}: ${r.value}`).join('\n');
  try {
    await navigator.clipboard.writeText(text);
    notify(t('info.copied'), 'success');
  } catch {
    notify(t('info.copy_failed'), 'warning');
  }
}

function infoView(d) {
  const body = html`
    <div class="info-head">
      <span class="info-badge">${d.kind === 'receiver' ? icons.receiver : icons.speaker}</span>
      <div>
        <div class="info-room">${d.room}</div>
        ${d.model ? html`<div class="secondary">${d.model}</div>` : nothing}
      </div>
    </div>
    <div class="card info-card">
      ${d.rows === null ? spinner() : nothing}
      ${d.error ? html`<p class="hint">${d.error}</p>` : nothing}
      ${(d.rows ?? []).map((r) => html`<div class="info-row">
        <span class="info-label">${r.label}</span>
        <span class="info-value" aria-label=${r.label}>${r.value}</span>
      </div>`)}
    </div>`;
  return dialogFrame(d.title, body, html`
    <button type="button" class="btn" ?disabled=${!d.rows?.length} @click=${() => copyRows(d.rows)}>${icons.copy}<span>${t('info.copy')}</span></button>
    <button type="submit" class="btn accent" data-autofocus>${t('info.close')}</button>`, { wide: true, onSubmit: () => closeDialog() });
}

function addSpeakerView(d) {
  const text = d.address.trim();
  const valid = isSpeakerAddress(text);
  const message = text.length === 0 ? t('dlg.speaker.example') : valid ? t('dlg.speaker.valid') : t('dlg.speaker.invalid');
  const submit = () => {
    if (valid) {
      closeDialog();
      addSpeaker(text);
    }
  };
  return dialogFrame(t('dlg.speaker.header'), html`
    <p class="hint dialog-text">${t('dlg.speaker.hint')}</p>
    <label class="field-label" for="speaker-address">${t('dlg.speaker.address')}</label>
    <input id="speaker-address" type="text" class="text-input full" data-autofocus spellcheck="false" .value=${live(d.address)}
      aria-label=${t('dlg.speaker.address_name')} title=${t('dlg.speaker.address_tooltip')}
      @input=${(e) => updateDialog({ address: e.target.value })}>
    <p class="hint ${text && !valid ? 'warning-text' : ''}">${message}</p>`, html`
    <button type="button" class="btn" @click=${() => closeDialog()}>${t('dlg.speaker.cancel')}</button>
    <button type="submit" class="btn accent" ?disabled=${!valid}>${t('dlg.speaker.add')}</button>`, { onSubmit: submit });
}

function addReceiverView(d) {
  const text = d.address.trim();
  const valid = isLocalIPv4(text);
  const message = d.message ?? (text.length === 0 ? t('dlg.receiver.example') : valid ? t('dlg.speaker.valid') : t('dlg.receiver.invalid'));
  const submit = async () => {
    if (!valid || d.adding) {
      return;
    }

    updateDialog({ adding: true });
    if (await addReceiver(text)) {
      closeDialog();
    } else {
      updateDialog({ adding: false });
    }
  };
  return dialogFrame(t('dlg.receiver.header'), html`
    <p class="hint dialog-text">${t('dlg.receiver.hint')}</p>
    <label class="field-label" for="receiver-maker">${t('dlg.receiver.manufacturer')}</label>
    <select id="receiver-maker" class="select full"><option selected>Pioneer (VSX)</option></select>
    <label class="field-label" for="receiver-address">${t('dlg.receiver.address')}</label>
    <div class="input-with-button">
      <input id="receiver-address" type="text" class="text-input" data-autofocus spellcheck="false" .value=${live(d.address)}
        @input=${(e) => updateDialog({ address: e.target.value, message: null })}>
      <button type="button" class="btn" ?disabled=${d.searching} @click=${searchReceivers}>${icons.search}<span>${t('dlg.receiver.search')}</span></button>
    </div>
    <p class="hint ${text && !valid && !d.message ? 'warning-text' : ''}">${message}</p>
    ${d.searching ? html`<div class="searching">${spinner('small')}<span>${t('dlg.receiver.searching')}</span></div>` : nothing}
    ${d.found.length > 0 ? html`<ul class="found-list">
      ${d.found.map((r) => html`<li><button type="button" class="found-row ${r.host === text ? 'selected' : ''}"
        @click=${() => updateDialog({ address: r.host })}>
        <strong>${r.name}</strong><span class="secondary">${[r.model, r.host].filter(Boolean).join(' · ')}</span>
      </button></li>`)}
    </ul>` : nothing}`, html`
    <button type="button" class="btn" @click=${() => closeDialog()}>${t('dlg.speaker.cancel')}</button>
    <button type="submit" class="btn accent" ?disabled=${!valid || d.adding}>${t('dlg.speaker.add')}</button>`, { onSubmit: submit });
}

function customStationView(d) {
  const valid = d.name.trim().length > 0 && /^https?:\/\/\S+$/i.test(d.url.trim());
  const submit = async () => {
    if (!valid || d.adding) {
      return;
    }

    updateDialog({ adding: true });
    if (await addCustomStation(d.name, d.url)) {
      closeDialog();
    } else {
      updateDialog({ adding: false });
    }
  };
  return dialogFrame(t('radio.custom_station'), html`
    <p class="hint dialog-text">${t('radio.custom_station_tooltip')}</p>
    <label class="field-label" for="station-name">${t('electron.radio.station_name')}</label>
    <input id="station-name" type="text" class="text-input full" data-autofocus maxlength="80" .value=${live(d.name)}
      @input=${(e) => updateDialog({ name: e.target.value })}>
    <label class="field-label" for="station-url">${t('electron.radio.stream_url')}</label>
    <input id="station-url" type="url" class="text-input full" spellcheck="false" placeholder="https://…" .value=${live(d.url)}
      @input=${(e) => updateDialog({ url: e.target.value })}>
    ${d.url.trim() && !/^https?:\/\/\S+$/i.test(d.url.trim()) ? html`<p class="hint warning-text">${t('radio.invalid_url')}</p>` : nothing}
    ${d.adding ? html`<div class="searching">${spinner('small')}</div>` : nothing}`, html`
    <button type="button" class="btn" @click=${() => closeDialog()}>${t('dlg.prompt.cancel')}</button>
    <button type="submit" class="btn accent" ?disabled=${!valid || d.adding}>${t('dlg.speaker.add')}</button>`, { onSubmit: submit });
}

const DIALOGS = {
  confirm: confirmView,
  prompt: promptView,
  info: infoView,
  addSpeaker: addSpeakerView,
  addReceiver: addReceiverView,
  customStation: customStationView,
};

export function dialogView() {
  const dialog = state.dialog;
  return dialog && DIALOGS[dialog.type] ? DIALOGS[dialog.type](dialog) : nothing;
}
