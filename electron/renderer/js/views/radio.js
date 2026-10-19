import { html, nothing } from '../../vendor/lit-html/lit-html.js';
import { live } from '../../vendor/lit-html/directives/live.js';
import { t } from '../i18n.js';
import { COUNTRY_CODES, stationDetails } from '../format.js';
import { state, invalidate } from '../state.js';
import { contextMenuHandler, openDialog } from '../ui.js';
import { showPage } from '../actions/core.js';
import {
  GENRES, canPlayStreams, countryName, deleteStation, isSaved, loadPopular, pickLogo, playStation, radioCountry,
  radioTargetName, renameStation, resetLogo, search, searchGenre, setCountry, stationLogo, toggleSaved,
} from '../actions/radio.js';
import { art, iconButton } from './common.js';
import { icons } from './icons.js';

let countryCache = { language: null, list: [] };

function countries() {
  const language = document.documentElement.lang;
  if (countryCache.language !== language) {
    const collator = new Intl.Collator(language || undefined, { sensitivity: 'base' });
    const list = COUNTRY_CODES.map((code) => ({ code, name: countryName(code) }))
      .filter((c) => c.name && c.name !== c.code)
      .sort((a, b) => collator.compare(a.name, b.name));
    countryCache = { language, list };
  }

  return countryCache.list;
}

function stationMenu(station) {
  const saved = isSaved(station);
  return [
    { label: t('radio.menu.play'), icon: icons.play, disabled: !canPlayStreams(), action: () => playStation(station) },
    'separator',
    ...(saved ? [
      { label: t('radio.menu.choose_logo'), icon: icons.image, action: () => pickLogo(station) },
      { label: t('radio.menu.reset_logo'), disabled: !station.logoUrl, action: () => resetLogo(station) },
      { label: t('radio.menu.rename'), icon: icons.edit, action: () => renameStation(station) },
      'separator',
    ] : []),
    { label: saved ? t('radio.remove_saved') : t('radio.add_saved'), icon: saved ? icons.starFilled : icons.star, action: () => toggleSaved(station) },
    ...(saved ? [{ label: t('radio.menu.delete'), icon: icons.trash, action: () => deleteStation(station) }] : []),
  ];
}

function stationRow(station, { savedList }) {
  const saved = isSaved(station);
  const details = stationDetails(station, t);
  return html`<li class="station-row" @contextmenu=${contextMenuHandler(() => stationMenu(station))}>
    ${art(stationLogo(station), { size: 44, className: 'contain', seed: station.name })}
    <span class="station-text">
      <span class="station-name">${station.name}</span>
      ${details ? html`<span class="station-details">${details}</span>` : nothing}
    </span>
    ${savedList ? html`
      ${iconButton({ icon: icons.image, label: t('radio.change_logo'), title: t('radio.change_logo_tooltip'), onClick: () => pickLogo(station) })}
      ${iconButton({ icon: icons.edit, label: t('radio.menu.rename'), onClick: () => renameStation(station) })}
      ${iconButton({ icon: icons.trash, label: t('radio.menu.delete'), onClick: () => deleteStation(station) })}` : nothing}
    ${iconButton({
      icon: saved ? icons.starFilled : icons.star,
      label: t('radio.toggle_saved'),
      title: saved ? t('radio.remove_saved') : t('radio.add_saved'),
      active: saved,
      onClick: () => toggleSaved(station),
    })}
    <button type="button" class="play-btn small" title=${t('radio.play_tooltip')} aria-label=${t('radio.play_station', station.name)}
      ?disabled=${!canPlayStreams()} @click=${() => playStation(station)}>${icons.play}</button>
  </li>`;
}

function openCustomStation() {
  openDialog({ type: 'customStation', name: '', url: '' });
}

export function radioView() {
  const radio = state.radio;
  const target = radioTargetName();
  const country = radioCountry();
  const collator = new Intl.Collator(document.documentElement.lang || undefined, { sensitivity: 'base' });
  const saved = [...state.stations].sort((a, b) => collator.compare(a.name, b.name));
  const header = typeof radio.header === 'function' ? radio.header() : t('radio.popular');
  return html`<div class="page radio-page">
    <div class="page-inner">
      <div class="page-head">
        <div class="page-title">
          ${iconButton({ icon: icons.back, label: t('radio.back'), onClick: () => showPage(state.selection.kind === 'receiver' ? 'receiver' : 'nowplaying') })}
          <h1>${t('radio.title')}</h1>
        </div>
        <span class="secondary small">${target ? t('radio.plays_in', target) : t('radio.no_room')}</span>
      </div>

      <div class="search-row">
        <input type="search" class="text-input" .value=${live(radio.query)} aria-label=${t('radio.search_name')} title=${t('radio.search_tooltip')}
          placeholder=${t('radio.search_name')}
          @input=${(e) => { radio.query = e.target.value; }}
          @keydown=${(e) => { if (e.key === 'Enter') { search(); } }}>
        <select class="select" aria-label=${t('radio.country')} @change=${(e) => setCountry(e.target.value)}>
          <option value="" ?selected=${!country}>${t('radio.all_countries')}</option>
          ${countries().map((c) => html`<option value=${c.code} ?selected=${c.code === country}>${c.name}</option>`)}
        </select>
        <button type="button" class="btn accent" @click=${search}>${icons.search}<span>${t('radio.search')}</span></button>
      </div>
      <div class="genres">
        ${GENRES.map((genre) => html`<button type="button" class="btn compact" @click=${() => searchGenre(genre)}>${genre}</button>`)}
      </div>
      ${!canPlayStreams() ? html`<p class="hint warning-text">${t('radio.no_stream_support')}</p>` : nothing}

      <section class="card">
        <div class="card-header">
          <h2 class="section-title">${t('radio.saved_title')}</h2>
          <div class="card-actions">
            <button type="button" class="btn compact" title=${t('radio.custom_station_tooltip')} @click=${openCustomStation}>
              ${icons.add}<span>${t('radio.custom_station')}</span>
            </button>
          </div>
        </div>
        ${saved.length === 0 ? html`<p class="hint">${t('radio.saved_empty')}</p>` : nothing}
        <ul class="station-list">${saved.map((s) => stationRow(s, { savedList: true }))}</ul>
      </section>

      <section class="card">
        <div class="card-header">
          <h2 class="section-title">${header}</h2>
          <div class="card-actions">
            ${iconButton({ icon: icons.trending, label: t('radio.popular'), title: t('radio.show_popular_tooltip'), onClick: () => { radio.query = ''; invalidate(); loadPopular(); } })}
          </div>
        </div>
        ${radio.loading ? html`<div class="progress indeterminate"></div>` : nothing}
        ${radio.emptyKey && !radio.loading ? html`<p class="secondary">${t(radio.emptyKey)}</p>` : nothing}
        <ul class="station-list">${radio.results.map((s) => stationRow(s, { savedList: false }))}</ul>
      </section>
      <p class="hint center">${t('radio.provider', 'Radio Browser')}</p>
    </div>
  </div>`;
}
