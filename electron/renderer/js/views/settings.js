import { html, nothing } from '../../vendor/lit-html/lit-html.js';
import { live } from '../../vendor/lit-html/directives/live.js';
import { LANGUAGE_NAMES, SUPPORTED_LANGUAGES, t } from '../i18n.js';
import { displayHotkey, hotkeyFromEvent } from '../format.js';
import { state, isMac } from '../state.js';
import { showPage } from '../actions/core.js';
import { checkUpdates, discardSettings, saveSettings, setDraftHotkey, updateDraft } from '../actions/settings.js';
import { checkbox, iconButton, slider, spinner } from './common.js';
import { icons } from './icons.js';

const HOTKEYS = [
  ['playPause', 'settings.hotkeys.play_pause', 'settings.hotkeys.play_pause_box'],
  ['next', 'settings.hotkeys.next', 'settings.hotkeys.next_box'],
  ['previous', 'settings.hotkeys.previous', 'settings.hotkeys.previous_box'],
  ['volumeUp', 'settings.hotkeys.volume_up', 'settings.hotkeys.volume_up_box'],
  ['volumeDown', 'settings.hotkeys.volume_down', 'settings.hotkeys.volume_down_box'],
];

function onHotkeyKey(name) {
  return (event) => {
    if (event.key === 'Tab' && !event.ctrlKey && !event.altKey && !event.metaKey) {
      return;
    }

    const text = hotkeyFromEvent(event);
    event.preventDefault();
    if (text !== null) {
      setDraftHotkey(name, text);
    }
  };
}

function link(label, url) {
  return html`<button type="button" class="link" @click=${() => window.sonosdesk.openExternal(url)}>${label}</button>`;
}

function row(label, control, hint) {
  return html`<div class="setting-row">
    <div class="setting-label">
      <span>${label}</span>
      ${hint ? html`<span class="hint">${hint}</span>` : nothing}
    </div>
    <div class="setting-control">${control}</div>
  </div>`;
}

function toggle(label, checked, onChange, hint) {
  return html`<div class="setting-check">
    ${checkbox({ label, checked, onChange })}
    ${hint ? html`<p class="hint indent">${hint}</p>` : nothing}
  </div>`;
}

function appearanceCard(d) {
  const mac = isMac();
  const systemLabel = mac ? t('electron.settings.system') : t('settings.language.system');
  return html`<section class="card">
    <h2 class="section-title">${t('settings.appearance.title')}</h2>
    ${row(t('settings.language.label'), html`<select class="select full" aria-label=${t('settings.language.label')}
        @change=${(e) => updateDraft({ language: e.target.value })}>
        <option value="" ?selected=${!d.language}>${systemLabel}</option>
        ${SUPPORTED_LANGUAGES.map((code) => html`<option value=${code} ?selected=${d.language === code}>${LANGUAGE_NAMES[code]}</option>`)}
      </select>`)}
    ${row(t('settings.theme.label'), html`<select class="select full" aria-label=${t('settings.theme.label')}
        @change=${(e) => updateDraft({ theme: e.target.value })}>
        <option value="system" ?selected=${d.theme === 'system'}>${mac ? t('electron.settings.system') : t('settings.theme.system')}</option>
        <option value="light" ?selected=${d.theme === 'light'}>${t('settings.theme.light')}</option>
        <option value="dark" ?selected=${d.theme === 'dark'}>${t('settings.theme.dark')}</option>
      </select>`)}
    ${toggle(mac ? t('electron.settings.start_at_login') : t('settings.start.with_windows'), d.startWithWindows, (v) => updateDraft({ startWithWindows: v }))}
    ${toggle(mac ? t('electron.settings.start_hidden_mac') : t('settings.start.minimized'), d.startMinimized, (v) => updateDraft({ startMinimized: v }))}
    ${mac ? nothing : toggle(t('settings.start.close_to_tray'), d.closeToTray, (v) => updateDraft({ closeToTray: v }), t('settings.start.close_to_tray_hint'))}
    ${toggle(t('electron.settings.media_keys'), d.windowsMediaControls, (v) => updateDraft({ windowsMediaControls: v }),
      mac ? t('electron.settings.media_keys_hint_mac') : t('electron.settings.media_keys_hint'))}
  </section>`;
}

function networkCard(d) {
  return html`<section class="card">
    <h2 class="section-title">${t('settings.network.title')}</h2>
    <label class="field-label" for="manual-addresses">${t('settings.network.manual_addresses')}</label>
    <textarea id="manual-addresses" class="text-input" rows="3" spellcheck="false" .value=${live(d.manualAddresses)}
      @input=${(e) => updateDraft({ manualAddresses: e.target.value })}></textarea>
    <p class="hint">${t('settings.network.manual_addresses_hint')}</p>
  </section>`;
}

function roomsCard(d) {
  return html`<section class="card">
    <h2 class="section-title">${t('settings.rooms.title')}</h2>
    ${row(t('settings.rooms.volume_step'), html`<div class="slider-control">
        ${slider({ value: d.volumeStep, min: 1, max: 15, step: 1, label: t('settings.rooms.volume_step_name'), onInput: (v) => updateDraft({ volumeStep: v }) })}
        <span class="volume-value">${d.volumeStep}</span>
      </div>`)}
    ${row(t('settings.rooms.volume_fade'), html`<div class="slider-control">
        ${slider({ value: d.volumeFadeSeconds, min: 0, max: 10, step: 0.5, label: t('settings.rooms.volume_fade'), onInput: (v) => updateDraft({ volumeFadeSeconds: v }) })}
        <span class="volume-value wide">${d.volumeFadeSeconds.toLocaleString(document.documentElement.lang || undefined, { maximumFractionDigits: 1 })} s</span>
      </div>`, t('settings.rooms.volume_fade_hint'))}
  </section>`;
}

function hotkeysCard(d) {
  const mac = isMac();
  return html`<section class="card">
    <h2 class="section-title">${t('settings.hotkeys.title')}</h2>
    ${toggle(t('settings.hotkeys.enabled'), d.hotkeysEnabled, (v) => updateDraft({ hotkeysEnabled: v }))}
    ${HOTKEYS.map(([name, labelKey, boxKey]) => row(t(labelKey), html`<input type="text" class="text-input hotkey-box" readonly
        .value=${live(displayHotkey(d.hotkeys[name], mac))} aria-label=${t(boxKey)} title=${t('app.hotkey_box_tooltip')}
        placeholder=${t('electron.settings.no_hotkey')} ?disabled=${!d.hotkeysEnabled} @keydown=${onHotkeyKey(name)}>`))}
    <p class="hint">${t('settings.hotkeys.hint')}</p>
  </section>`;
}

function updatesCard(d) {
  const update = state.update;
  return html`<section class="card">
    <h2 class="section-title">${t('settings.updates.title')}</h2>
    <p>${t('settings.updates.current', state.app.version || state.version)}</p>
    ${toggle(t('settings.updates.check_on_start'), d.checkForUpdates, (v) => updateDraft({ checkForUpdates: v }), t('settings.updates.check_on_start_hint'))}
    <div class="button-row">
      <button type="button" class="btn" ?disabled=${update.checking} @click=${() => checkUpdates({ manual: true })}>${t('settings.updates.check_now')}</button>
      ${update.info ? html`<button type="button" class="btn accent" @click=${() => window.sonosdesk.openExternal(update.info.url)}>${t('update.download')}</button>` : nothing}
      ${update.checking ? spinner('small') : nothing}
    </div>
    ${update.status ? html`<p class="hint">${update.status}</p>` : nothing}
  </section>`;
}

function aboutCard() {
  return html`<section class="card about">
    <h2 class="section-title">${t('settings.about.title')}</h2>
    <p>${t('settings.about.developed_by')} ${link('adriankrypton', 'https://github.com/adriankrypton')} (github.com/adriankrypton).</p>
    <p class="hint">${t('settings.about.license')}</p>
    <h3 class="subheading">${t('settings.about.sources_title')}</h3>
    <p class="hint"><strong>${t('settings.about.sonos_label')}</strong> ${t('settings.about.sonos_text')}</p>
    <p class="hint"><strong>${t('settings.about.radio_label')}</strong> ${t('settings.about.radio_text')}
      ${link('Radio Browser (radio-browser.info)', 'https://www.radio-browser.info/')}${t('settings.about.radio_text_after')}</p>
    <p class="hint"><strong>${t('settings.about.now_playing_label')}</strong> ${t('settings.about.now_playing_text')}</p>
    <p class="hint"><strong>${t('settings.about.software_label')}</strong> ${t('electron.about.software_text')}</p>
    <div class="button-row">
      <button type="button" class="btn" @click=${() => window.sonosdesk.openLogs()}>${t('settings.about.open_logs')}</button>
    </div>
  </section>`;
}

export function settingsView() {
  const d = state.settingsDraft;
  return html`<div class="page settings-page">
    <div class="page-head padded">
      <div class="page-title">
        ${iconButton({ icon: icons.back, label: t('common.back'), onClick: () => showPage(state.selection.kind === 'receiver' ? 'receiver' : 'nowplaying') })}
        <h1>${t('settings.title')}</h1>
      </div>
    </div>
    <div class="settings-scroll">
      <div class="page-inner narrow">
        ${d ? html`${appearanceCard(d)}${networkCard(d)}${roomsCard(d)}${hotkeysCard(d)}${updatesCard(d)}${aboutCard()}` : spinner()}
      </div>
    </div>
    <div class="save-bar">
      <button type="button" class="btn" ?disabled=${!d} @click=${discardSettings}>${t('common.discard')}</button>
      <button type="button" class="btn accent" ?disabled=${!d} @click=${saveSettings}>${t('common.save')}</button>
    </div>
  </div>`;
}
