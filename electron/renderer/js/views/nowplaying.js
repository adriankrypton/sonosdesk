import { html, nothing } from '../../vendor/lit-html/lit-html.js';
import { t } from '../i18n.js';
import { emptyRoomsText, formatTime, sourceText } from '../format.js';
import {
  state, groupPosition, groupVolume, has, isGroupBuffering, isGroupPlaying, memberVolume, selectedGroup, sleepRemaining,
} from '../state.js';
import { openMenuAt } from '../ui.js';
import {
  clearQueue, cycleRepeat, dissolveGroup, isReceiverLinkedTo, loadFavorites, loadQueue, nextTrack, partyMode, playFavorite,
  playQueueItem, previousTrack, removeQueueItem, rescan, resetEqualizer, seek, setGroupVolume, setMemberVolume, setSleepTimer,
  setSound, setSoundRoom, toggleCrossfade, toggleGroupMute, toggleMemberMute, togglePlay, toggleReceiverLink,
  toggleRoomMembership, toggleShuffle, toggleSoundExpanded,
} from '../actions/sonos.js';
import { art, cardHeader, checkbox, chip, expander, iconButton, playPauseButton, slider, spinner } from './common.js';
import { icons } from './icons.js';

const SLEEP_MINUTES = [15, 30, 45, 60, 90, 120];

function emptyView() {
  return html`<div class="page empty-page">
    <div class="empty-center">
      <span class="empty-icon">${icons.speaker}</span>
      <h2>${t('np.no_rooms')}</h2>
      <p class="secondary">${emptyRoomsText(state.discovery, t)}</p>
      <button type="button" class="btn accent" @click=${rescan}>${t('np.rescan')}</button>
    </div>
  </div>`;
}

function sleepMenu(group, button) {
  const remaining = sleepRemaining(group);
  openMenuAt(button, [
    { label: remaining ? t('group.sleep_remaining', formatTime(remaining)) : t('group.sleep_off'), header: true },
    'separator',
    ...SLEEP_MINUTES.map((minutes) => ({ label: t('np.sleep_minutes', minutes), action: () => setSleepTimer(group, minutes) })),
    'separator',
    { label: t('np.sleep_off'), disabled: !remaining, action: () => setSleepTimer(group, 0) },
  ]);
}

function progressView(group) {
  const playback = group.playback;
  const duration = playback?.durationSeconds;
  if (!(duration > 0)) {
    return nothing;
  }

  const position = groupPosition(group) ?? 0;
  const canSeek = playback.canSeek && has('seek');
  return html`<div class="progress-row">
    <span class="time">${formatTime(groupPosition(group))}</span>
    ${canSeek
      ? slider({ value: position, min: 0, max: duration, step: 1, label: t('np.seek'), title: t('np.seek_tooltip'), onInput: (v) => seek(group, v) })
      : html`<div class="progress" role="progressbar" aria-label=${t('np.progress')} aria-valuemin="0" aria-valuemax="100"
          aria-valuenow=${Math.round((position / duration) * 100)}><span style=${`width:${(position / duration) * 100}%`}></span></div>`}
    <span class="time">${formatTime(duration)}</span>
  </div>`;
}

function transportView(group, playing, buffering) {
  const playback = group.playback;
  const canMode = Boolean(playback?.canChangePlayMode);
  const repeat = playback?.repeat ?? 'off';
  const remaining = sleepRemaining(group);
  return html`<div class="transport">
    ${iconButton({ icon: icons.shuffle, label: t('np.shuffle'), active: Boolean(playback?.shuffle), disabled: !canMode, onClick: () => toggleShuffle(group) })}
    ${iconButton({ icon: icons.previous, label: t('np.previous'), title: t('np.previous_tooltip'), className: 'large', disabled: playback ? !playback.canSkipPrevious : false, onClick: () => previousTrack(group) })}
    ${playPauseButton({
      playing,
      buffering,
      label: playing ? t('vm.pause') : t('vm.play'),
      title: t('np.play_pause_tooltip'),
      onClick: () => togglePlay(group),
    })}
    ${iconButton({ icon: icons.next, label: t('np.next'), title: t('np.next_tooltip'), className: 'large', disabled: playback ? !playback.canSkipNext : false, onClick: () => nextTrack(group) })}
    ${iconButton({ icon: repeat === 'one' ? icons.repeatOne : icons.repeat, label: t('np.repeat'), title: t('np.repeat_tooltip'), active: repeat !== 'off', disabled: !canMode, onClick: () => cycleRepeat(group) })}
    ${playback?.crossfade !== null && playback?.crossfade !== undefined && has('crossfade')
      ? iconButton({ icon: icons.crossfade, label: t('np.crossfade'), title: t('np.crossfade_tooltip'), active: playback.crossfade === true, onClick: () => toggleCrossfade(group) })
      : nothing}
    ${has('sleepTimer') ? html`<button type="button" class="icon-btn wide ${remaining ? 'active' : ''}" aria-label=${t('np.sleep_timer')} aria-haspopup="menu"
        title=${remaining ? t('group.sleep_remaining', formatTime(remaining)) : t('group.sleep_off')}
        @click=${(e) => sleepMenu(group, e.currentTarget)}>
        ${icons.moon}${remaining ? html`<span class="countdown">${formatTime(remaining)}</span>` : nothing}
      </button>` : nothing}
  </div>`;
}

function heroView(group) {
  const playback = group.playback;
  const playing = isGroupPlaying(group);
  const buffering = isGroupBuffering(group);
  const source = sourceText(playback, t);
  const volume = groupVolume(group);
  return html`<section class="hero">
    ${art(playback?.artUrl, { size: 260, className: 'hero-art', seed: group.name })}
    <div class="hero-info">
      <div class="hero-head">
        <span class="hero-room">${group.name}</span>
        ${group.isGroup ? html`<span class="pill" title=${t('group.rooms_text', group.members.map((m) => m.room).join(', '))}>${icons.link}${t('group.badge', group.members.length)}</span>` : nothing}
      </div>
      ${buffering ? html`<div class="buffering">${spinner('small')}<span>${t('np.buffering')}</span></div>` : nothing}
      ${playback?.nowPlayingHint ? html`<div class="now-hint">${icons.music}<span>${playback.nowPlayingHint}</span></div>` : nothing}
      <h1 class="hero-title">${playback?.displayTitle || t('np.nothing_playing')}</h1>
      ${playback?.displayArtist ? html`<div class="hero-artist">${playback.displayArtist}</div>` : nothing}
      ${playback?.album && !playback.isRadio ? html`<div class="hero-album">${playback.album}</div>` : nothing}
      ${source ? html`<span class="source-pill">${source}</span>` : nothing}
      ${group.error ? html`<p class="error-text">${group.error}</p>` : nothing}
      ${progressView(group)}
      ${transportView(group, playing, buffering)}
      <div class="volume-row">
        ${iconButton({ icon: group.muted ? icons.mute : icons.volume, label: t('np.mute'), title: t('np.mute_tooltip'), active: group.muted, onClick: () => toggleGroupMute(group) })}
        ${slider({ value: volume, label: t('np.volume'), title: t('np.volume_tooltip'), disabled: group.volumeFixed, onInput: (v) => setGroupVolume(group, v) })}
        <span class="volume-value">${volume}</span>
      </div>
      ${group.volumeFixed ? html`<p class="hint">${t('np.fixed_volume')}</p>` : nothing}
    </div>
  </section>`;
}

function favoritesCard(group) {
  if (!has('favorites')) {
    return nothing;
  }

  const favorites = state.favorites;
  return html`<section class="card">
    ${cardHeader(t('np.favorites'), t('np.favorites_hint', group.name),
      favorites.loading ? spinner('small') : iconButton({ icon: icons.refresh, label: t('np.reload_favorites'), onClick: loadFavorites }))}
    ${favorites.loaded && favorites.items.length === 0 ? html`<p class="hint">${t('np.no_favorites')}</p>` : nothing}
    <div class="tiles">
      ${favorites.items.map((favorite) => html`<button type="button" class="tile" title=${favorite.description || favorite.title}
        aria-label=${favorite.title} @click=${() => playFavorite(favorite)}>
        ${art(favorite.artUrl, { size: 104, seed: favorite.title })}
        <span class="tile-label">${favorite.title}</span>
      </button>`)}
    </div>
  </section>`;
}

function roomVolumesCard(group) {
  if (!group.isGroup) {
    return nothing;
  }

  return html`<section class="card">
    ${cardHeader(t('np.room_volumes'))}
    <div class="member-volumes">
      ${group.members.map((member) => {
        const volume = memberVolume(member);
        return html`<div class="member-volume">
          <div class="member-label"><span>${member.room}</span><span class="hint">${member.model ?? ''}</span></div>
          ${iconButton({ icon: member.muted ? icons.mute : icons.volume, label: t('np.mute_room', member.room), active: member.muted, onClick: () => toggleMemberMute(member) })}
          ${slider({ value: volume, label: t('np.room_volume', member.room), disabled: member.fixed, onInput: (v) => setMemberVolume(member, v) })}
          <span class="volume-value">${volume}</span>
        </div>`;
      })}
    </div>
  </section>`;
}

function queueCard(group) {
  if (!has('queue')) {
    return nothing;
  }

  const queue = state.queue.groupId === group.id ? state.queue : { items: [], total: 0, loading: state.queue.loading };
  const current = group.playback?.source === 'queue' ? group.playback.queuePosition : null;
  const summary = queue.total === 0 ? t('np.queue_empty')
    : queue.total > queue.items.length ? t('queue.summary_partial', queue.items.length, queue.total) : t('queue.summary', queue.total);
  return html`<section class="card">
    ${cardHeader(t('np.queue'), summary, html`
      ${queue.loading ? spinner('small') : nothing}
      ${iconButton({ icon: icons.refresh, label: t('np.queue_reload'), onClick: loadQueue })}
      ${iconButton({ icon: icons.trash, label: t('np.queue_clear'), disabled: queue.items.length === 0, onClick: clearQueue })}`)}
    ${queue.items.length > 0 ? html`<ol class="queue-list">
      ${queue.items.map((item) => {
        const isCurrent = item.position === current;
        const subtitle = [item.artist, item.album].filter(Boolean).join(' · ');
        return html`<li class="queue-row ${isCurrent ? 'current' : ''}" title=${t('np.queue_play_tooltip')} @dblclick=${() => playQueueItem(item)}>
          <span class="queue-pos">${isCurrent ? icons.volume : item.position}</span>
          <span class="queue-text">
            <span class="queue-title">${item.title}</span>
            ${subtitle ? html`<span class="queue-sub">${subtitle}</span>` : nothing}
          </span>
          ${iconButton({ icon: icons.play, label: t('np.queue_play'), className: 'small', onClick: () => playQueueItem(item) })}
          ${iconButton({ icon: icons.close, label: t('np.queue_remove'), className: 'small', onClick: () => removeQueueItem(item) })}
        </li>`;
      })}
    </ol>` : nothing}
  </section>`;
}

function soundBody(group) {
  const sound = state.sound;
  const data = sound.data;
  return html`
    ${group.isGroup ? html`<div class="form-row">
      <label for="sound-room">${t('np.sound_room')}</label>
      <select id="sound-room" class="select" @change=${(e) => setSoundRoom(e.target.value)}>
        ${group.members.map((m) => html`<option value=${m.id} ?selected=${m.id === sound.playerId}>${m.room}</option>`)}
      </select>
    </div>` : nothing}
    ${sound.loading && !data ? spinner() : nothing}
    ${data ? html`
      <div class="form-row slider-row">
        <span>${t('np.sound_bass')}</span>
        ${slider({ value: data.bass ?? 0, min: -10, max: 10, step: 1, ticks: true, label: t('np.sound_bass'), onInput: (v) => setSound('bass', v) })}
        <span class="volume-value">${data.bass ?? 0}</span>
      </div>
      <div class="form-row slider-row">
        <span>${t('np.sound_treble')}</span>
        ${slider({ value: data.treble ?? 0, min: -10, max: 10, step: 1, ticks: true, label: t('np.sound_treble'), onInput: (v) => setSound('treble', v) })}
        <span class="volume-value">${data.treble ?? 0}</span>
      </div>
      <div class="checks">
        ${checkbox({ label: t('np.sound_loudness'), title: t('np.sound_loudness_tooltip'), checked: Boolean(data.loudness), onChange: (v) => setSound('loudness', v) })}
        ${data.nightMode !== null && data.nightMode !== undefined
          ? checkbox({ label: t('np.sound_night'), title: t('np.sound_night_tooltip'), checked: Boolean(data.nightMode), onChange: (v) => setSound('nightMode', v) })
          : nothing}
        ${data.speechEnhancement !== null && data.speechEnhancement !== undefined
          ? checkbox({ label: t('np.sound_speech'), title: t('np.sound_speech_tooltip'), checked: Boolean(data.speechEnhancement), onChange: (v) => setSound('speechEnhancement', v) })
          : nothing}
      </div>
      <button type="button" class="btn" @click=${resetEqualizer}>${t('np.sound_reset')}</button>` : nothing}`;
}

function soundCard(group) {
  if (!has('soundSettings')) {
    return nothing;
  }

  return expander({
    id: 'sound-body',
    title: t('np.sound'),
    hint: t('np.sound_hint'),
    expanded: state.sound.expanded,
    onToggle: toggleSoundExpanded,
    body: () => soundBody(group),
  });
}

function groupingCard(group) {
  if (!has('grouping')) {
    return nothing;
  }

  const canParty = state.rooms.filter((r) => r.controllable).length > 1;
  return html`<section class="card">
    ${cardHeader(t('np.group_rooms'), t('np.group_rooms_hint'), html`
      ${canParty ? html`<button type="button" class="btn" title=${t('np.party_tooltip')} @click=${() => partyMode(group)}>${icons.speakers}<span>${t('np.party_mode')}</span></button>` : nothing}
      ${group.isGroup ? html`<button type="button" class="btn" @click=${() => dissolveGroup(group)}>${t('np.dissolve_group')}</button>` : nothing}`)}
    <div class="chips">
      ${state.rooms.map((room) => chip({
        label: room.name,
        checked: group.members.some((m) => m.id === room.id),
        disabled: state.busyChips.has(room.id) || room.controllable === false,
        onToggle: () => toggleRoomMembership(room, group),
      }))}
    </div>
    ${state.receivers.length > 0 ? html`<div class="chips">
      ${state.receivers.map((receiver) => chip({
        label: receiver.name,
        checked: isReceiverLinkedTo(receiver, group),
        disabled: state.busyChips.has(`receiver:${receiver.id}`),
        leading: html`<span class="chip-icon">${icons.receiver}</span>`,
        title: t('receiver.chip_tooltip'),
        onToggle: () => toggleReceiverLink(receiver, group),
      }))}
    </div>` : nothing}
  </section>`;
}

export function nowPlayingView() {
  const group = selectedGroup();
  if (!group) {
    return emptyView();
  }

  return html`<div class="page">
    <div class="page-inner">
      ${heroView(group)}
      ${favoritesCard(group)}
      ${roomVolumesCard(group)}
      ${queueCard(group)}
      ${soundCard(group)}
      ${groupingCard(group)}
    </div>
  </div>`;
}
