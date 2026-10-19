import { html, nothing } from '../../vendor/lit-html/lit-html.js';
import { t } from '../i18n.js';
import { discoveryText, emptyRoomsText, groupSubtitle, hasDiscoveryProblem, receiverDisplay } from '../format.js';
import { state, isGroupBuffering, isGroupPlaying, receiverLocal } from '../state.js';
import { contextMenuHandler, openDialog } from '../ui.js';
import { selectGroup, selectReceiver, showPage } from '../actions/core.js';
import { pauseAll, rescan, showSpeakerInfo } from '../actions/sonos.js';
import { removeReceiver, showReceiverInfo, togglePower } from '../actions/receivers.js';
import { art, spinner } from './common.js';
import { icons } from './icons.js';

const clock = (date) => date.toLocaleTimeString(document.documentElement.lang || undefined, { hour: '2-digit', minute: '2-digit' });

function modelOf(playerId) {
  return state.rooms.find((r) => r.id === playerId)?.model
    ?? state.groups.flatMap((g) => g.members ?? []).find((m) => m.id === playerId)?.model
    ?? null;
}

export function openAddSpeaker() {
  openDialog({ type: 'addSpeaker', address: '' });
}

export function openAddReceiver() {
  openDialog({ type: 'addReceiver', address: '', searching: false, found: [], message: null });
}

function listMenuItems() {
  return [
    { label: t('main.add_speaker_by_ip'), icon: icons.add, action: openAddSpeaker },
    { label: t('main.add_receiver_by_ip'), icon: icons.receiver, action: openAddReceiver },
    { label: t('main.rescan'), icon: icons.refresh, shortcut: 'F5', action: rescan },
    'separator',
    { label: t('main.manage_addresses'), icon: icons.settings, action: () => showPage('settings') },
  ];
}

function groupMenuItems(group) {
  const members = group.members ?? [];
  const info = group.isGroup
    ? members.map((m) => ({ label: `${t('main.speaker_info')}: ${m.room}`, icon: icons.info, action: () => showSpeakerInfo(m.id, m.room, m.model) }))
    : [{ label: t('main.speaker_info'), icon: icons.info, action: () => showSpeakerInfo(group.coordinatorId, group.coordinatorRoom, modelOf(group.coordinatorId)) }];
  return [...info, 'separator', { label: t('main.add_speaker_by_ip'), action: openAddSpeaker }];
}

function receiverMenuItems(receiver) {
  return [
    { label: t('main.speaker_info'), icon: icons.info, action: () => showReceiverInfo(receiver) },
    { label: receiver.on ? t('receiver.power_off') : t('receiver.power_on'), icon: icons.power, action: () => togglePower(receiver) },
    'separator',
    { label: t('receiver.remove'), icon: icons.trash, action: () => removeReceiver(receiver) },
  ];
}

function isSelected(kind, id) {
  return state.selection.kind === kind && state.selection.id === id && (state.page === 'nowplaying' || state.page === 'receiver');
}

function groupRow(group) {
  const playing = isGroupPlaying(group);
  const buffering = isGroupBuffering(group);
  const selected = isSelected('group', group.id);
  const rooms = group.isGroup ? t('group.rooms_text', (group.members ?? []).map((m) => m.room).join(', ')) : null;
  return html`<li class="room-item">
    <button type="button" class="room-row ${selected ? 'selected' : ''}" aria-current=${selected ? 'true' : 'false'}
      title=${rooms ?? nothing} @click=${() => selectGroup(group.id)} @contextmenu=${contextMenuHandler(() => groupMenuItems(group))}>
      ${art(group.playback?.artUrl, { size: 48, seed: group.name, placeholder: icons.speaker })}
      <span class="room-text">
        <span class="room-name">${group.name}</span>
        <span class="room-sub">${groupSubtitle(group, { playing, buffering }, t)}</span>
        ${group.isGroup ? html`<span class="badge">${icons.link}<span>${t('group.badge', (group.members ?? []).length)}</span></span>` : nothing}
      </span>
      ${playing ? html`<span class="room-playing" title=${t('main.playing_now')}>${icons.volume}</span>` : nothing}
    </button>
    ${group.isGroup ? html`<ul class="member-list">
      ${(group.members ?? []).map((member) => html`<li>
        <button type="button" class="member-row" @click=${() => selectGroup(group.id)}
          @contextmenu=${contextMenuHandler(() => [{ label: t('main.speaker_info'), icon: icons.info, action: () => showSpeakerInfo(member.id, member.room, member.model) }])}>
          ${icons.speaker}<span>${member.room}</span>
        </button>
      </li>`)}
    </ul>` : nothing}
  </li>`;
}

function receiverRow(receiver) {
  const local = receiverLocal(receiver.id);
  const display = receiverDisplay(receiver, { buffering: local.buffering }, t);
  const selected = isSelected('receiver', receiver.id);
  return html`<li class="room-item">
    <button type="button" class="room-row ${selected ? 'selected' : ''}" aria-current=${selected ? 'true' : 'false'}
      @click=${() => selectReceiver(receiver.id)} @contextmenu=${contextMenuHandler(() => receiverMenuItems(receiver))}>
      ${art(receiver.playing ? receiver.artUrl : null, { size: 48, className: 'contain', seed: receiver.name, placeholder: icons.receiver })}
      <span class="room-text">
        <span class="room-name">${receiver.name}</span>
        <span class="room-sub">${display.subtitle}</span>
        ${receiver.linkedRoom ? html`<span class="badge">${icons.link}<span>${t('receiver.linked_badge', receiver.linkedRoom)}</span></span>` : nothing}
      </span>
      ${receiver.playing ? html`<span class="room-playing" title=${t('main.playing_now')}>${icons.volume}</span>` : nothing}
    </button>
  </li>`;
}

export function sidebarView() {
  const discovery = state.discovery;
  const searching = discovery?.phase === 'searching';
  const problem = hasDiscoveryProblem(discovery);
  const hasItems = state.groups.length > 0 || state.receivers.length > 0;
  return html`<aside class="sidebar">
    <div class="brand">
      <img src="icon.png" width="32" height="32" alt="">
      <div>
        <div class="brand-name">SonosDesk</div>
        <div class="brand-sub">${state.backend}</div>
      </div>
    </div>
    <nav class="side-nav" aria-label=${t('electron.navigation')}>
      <button type="button" class="nav-item ${state.page === 'nowplaying' || state.page === 'receiver' ? 'current' : ''}"
        @click=${() => showPage(state.selection.kind === 'receiver' ? 'receiver' : 'nowplaying')}>
        ${icons.speakers}<span>${t('group.now_playing')}</span>
      </button>
      <button type="button" class="nav-item ${state.page === 'radio' ? 'current' : ''}" title=${t('main.radio_tooltip')} @click=${() => showPage('radio')}>
        ${icons.music}<span>${t('main.radio')}</span>
      </button>
      <button type="button" class="nav-item ${state.page === 'settings' ? 'current' : ''}" title=${t('main.settings_tooltip')} @click=${() => showPage('settings')}>
        ${icons.settings}<span>${t('main.settings')}</span>
      </button>
    </nav>
    <div class="sidebar-head">
      <h2 class="section-title">${t('main.rooms')}</h2>
      <button type="button" class="icon-btn small" title=${t('main.rescan_tooltip')} aria-label=${t('main.rescan_short')} @click=${rescan}>
        ${searching ? spinner() : icons.refresh}
      </button>
    </div>
    <div class="room-scroll" @contextmenu=${contextMenuHandler(listMenuItems)}>
      ${hasItems
        ? html`<ul class="room-list" aria-label=${t('main.rooms_and_groups')}>
            ${state.groups.map(groupRow)}${state.receivers.map(receiverRow)}
          </ul>`
        : html`<div class="room-empty">
            ${searching || !discovery ? html`<div class="progress indeterminate"></div>` : nothing}
            <p class="secondary">${emptyRoomsText(discovery, t)}</p>
            ${problem ? html`<p class="hint">${t('main.discovery_tip')}</p>` : nothing}
          </div>`}
    </div>
    <div class="sidebar-footer">
      <button type="button" class="btn wide subtle" title=${t('main.pause_all_tooltip')} ?disabled=${state.groups.length === 0} @click=${pauseAll}>
        ${icons.pauseOutline}<span>${t('main.pause_all')}</span>
      </button>
      <div class="discovery-status" title=${discoveryText(discovery, t, clock)}>
        <span class="dot ${searching ? 'searching' : problem ? 'problem' : 'ok'}"></span>
        <span>${discoveryText(discovery, t, clock)}</span>
      </div>
    </div>
  </aside>`;
}
