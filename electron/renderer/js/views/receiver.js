import { html, nothing } from '../../vendor/lit-html/lit-html.js';
import { t } from '../i18n.js';
import { receiverDisplay } from '../format.js';
import { state, receiverLocal, receiverVolume, selectedReceiver } from '../state.js';
import {
  canPlayReceiver, linkReceiver, playOnReceiver, resetTone, setListeningMode, setReceiverVolume, setTone,
  toggleReceiverMute, toggleReceiverPlay, toggleReceiverSound,
} from '../actions/receivers.js';
import { stationLogo } from '../actions/radio.js';
import { art, cardHeader, chip, expander, iconButton, playPauseButton, slider, spinner } from './common.js';
import { icons } from './icons.js';

const TONE_MIN = -6;
const TONE_MAX = 6;

function heroView(receiver) {
  const local = receiverLocal(receiver.id);
  const display = receiverDisplay(receiver, { buffering: local.buffering }, t);
  const volume = receiverVolume(receiver);
  return html`<section class="hero">
    ${art(receiver.playing ? receiver.artUrl : null, { size: 260, className: 'hero-art contain', seed: receiver.name, placeholder: icons.receiver })}
    <div class="hero-info">
      <div class="hero-head">
        <span class="hero-room">${receiver.name}</span>
        ${receiver.linkedRoom ? html`<span class="pill">${icons.link}${t('receiver.linked_badge', receiver.linkedRoom)}</span>` : nothing}
      </div>
      ${local.buffering ? html`<div class="buffering">${spinner('small')}<span>${t('np.buffering')}</span></div>` : nothing}
      <h1 class="hero-title">${display.title || t('np.nothing_playing')}</h1>
      ${display.artist ? html`<div class="hero-artist">${display.artist}</div>` : nothing}
      <span class="source-pill">${display.source}</span>
      ${!receiver.reachable ? html`<p class="error-text">${t('group.unreachable')}</p>` : nothing}
      <div class="transport">
        ${iconButton({ icon: icons.shuffle, label: t('np.shuffle'), disabled: true })}
        ${iconButton({ icon: icons.previous, label: t('np.previous'), className: 'large', disabled: true })}
        ${playPauseButton({
          playing: receiver.playing,
          buffering: local.buffering,
          disabled: !canPlayReceiver(receiver),
          label: receiver.playing ? t('vm.pause') : t('vm.play'),
          title: t('np.play_pause_tooltip'),
          onClick: () => toggleReceiverPlay(receiver),
        })}
        ${iconButton({ icon: icons.next, label: t('np.next'), className: 'large', disabled: true })}
        ${iconButton({ icon: icons.repeat, label: t('np.repeat'), title: t('np.repeat_tooltip'), disabled: true })}
      </div>
      <div class="volume-row">
        ${iconButton({ icon: receiver.muted ? icons.mute : icons.volume, label: t('np.mute'), title: t('np.mute_tooltip'), active: receiver.muted, onClick: () => toggleReceiverMute(receiver) })}
        ${slider({ value: volume, label: t('np.volume'), title: t('np.volume_tooltip'), disabled: !receiver.on, onInput: (v) => setReceiverVolume(receiver, v) })}
        <span class="volume-value">${volume}</span>
      </div>
    </div>
  </section>`;
}

function stationsCard(receiver) {
  return html`<section class="card">
    ${cardHeader(t('receiver.stations'), t('np.favorites_hint', receiver.name))}
    ${state.stations.length === 0 ? html`<p class="hint">${t('receiver.no_stations')}</p>` : nothing}
    <div class="tiles">
      ${state.stations.map((station) => html`<button type="button" class="tile" title=${station.name} aria-label=${station.name}
        @click=${() => playOnReceiver(receiver, station)}>
        ${art(stationLogo(station), { size: 104, className: 'contain', seed: station.name })}
        <span class="tile-label">${station.name}</span>
      </button>`)}
    </div>
  </section>`;
}

function toneSlider(receiver, setting, label, value, disabled) {
  return html`<div class="form-row slider-row">
    <span>${label}</span>
    ${slider({ value: value ?? 0, min: TONE_MIN, max: TONE_MAX, step: 1, ticks: true, label, disabled, onInput: (v) => setTone(receiver, setting, v) })}
    <span class="volume-value">${value ?? 0}</span>
  </div>`;
}

function soundBody(receiver) {
  const sound = state.receiverSound;
  const data = sound.receiverId === receiver.id ? sound.data : null;
  if (!data) {
    return sound.loading ? spinner() : nothing;
  }

  const modes = Array.isArray(data.modes) ? data.modes : [];
  const toneDisabled = !data.toneAvailable;
  return html`
    ${modes.length > 0 ? html`<div class="form-row">
      <label for="listening-mode">${t('receiver.listening_mode')}</label>
      <select id="listening-mode" class="select wide" @change=${(e) => setListeningMode(receiver, e.target.value)}>
        ${data.mode && !modes.some((m) => m.code === data.mode) ? html`<option value=${data.mode} selected>${data.mode}</option>` : nothing}
        ${modes.map((m) => html`<option value=${m.code} ?selected=${m.code === data.mode}>${m.name}</option>`)}
      </select>
    </div>` : nothing}
    ${toneDisabled ? html`<p class="hint">${t('receiver.tone_mode_unavailable')}</p>` : nothing}
    ${data.bass !== null || data.treble !== null ? html`
      ${toneSlider(receiver, 'bass', t('np.sound_bass'), data.bass, toneDisabled)}
      ${toneSlider(receiver, 'treble', t('np.sound_treble'), data.treble, toneDisabled)}
      <button type="button" class="btn" ?disabled=${toneDisabled} @click=${() => resetTone(receiver)}>${t('np.sound_reset')}</button>` : nothing}`;
}

function soundCard(receiver) {
  if (!receiver.on) {
    return nothing;
  }

  const sound = state.receiverSound;
  return expander({
    id: 'receiver-sound',
    title: t('np.sound'),
    hint: t('receiver.sound_hint'),
    expanded: sound.receiverId === receiver.id && sound.expanded,
    onToggle: () => toggleReceiverSound(receiver),
    body: () => soundBody(receiver),
  });
}

function roomNames(receiver) {
  const names = [...new Set(state.rooms.map((r) => r.name))];
  if (receiver.linkedRoom && !names.includes(receiver.linkedRoom)) {
    names.push(receiver.linkedRoom);
  }

  return names.sort((a, b) => a.localeCompare(b, document.documentElement.lang || undefined, { sensitivity: 'base' }));
}

function groupingCard(receiver) {
  return html`<section class="card">
    ${cardHeader(t('np.group_rooms'), t('receiver.group_hint'))}
    <div class="chips">
      ${roomNames(receiver).map((room) => {
        const linked = room === receiver.linkedRoom;
        return chip({ label: room, checked: linked, onToggle: () => linkReceiver(receiver, linked ? null : room) });
      })}
    </div>
    ${receiver.syncNotice ? html`<p class="hint">${receiver.syncNotice}</p>` : nothing}
  </section>`;
}

export function receiverView() {
  const receiver = selectedReceiver();
  if (!receiver) {
    return nothing;
  }

  return html`<div class="page">
    <div class="page-inner">
      ${heroView(receiver)}
      ${stationsCard(receiver)}
      ${soundCard(receiver)}
      ${groupingCard(receiver)}
    </div>
  </div>`;
}
