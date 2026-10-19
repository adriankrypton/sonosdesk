import { html, nothing } from '../../vendor/lit-html/lit-html.js';
import { keyed } from '../../vendor/lit-html/directives/keyed.js';
import { live } from '../../vendor/lit-html/directives/live.js';
import { assetUrl } from '../api.js';
import { icons } from './icons.js';

function hideBrokenImage(event) {
  event.target.hidden = true;
}

// Rounded artwork with placeholder (WPF AlbumArtTemplate). The placeholder shows until the image loads.
// With a seed (room or station name) the placeholder gets its own colour, like tiles in the Sonos app.
export function art(url, { size = 44, placeholder = icons.music, className = '', seed = null } = {}) {
  const src = assetUrl(url);
  const hue = seed ? hueOf(seed) : null;
  return html`<span class="art ${className} ${hue === null ? '' : 'seeded'}" style=${`--size:${size}px${hue === null ? '' : `;--ph-h:${hue}`}`}>
    <span class="art-placeholder">${placeholder}</span>
    ${src ? keyed(src, html`<img src=${src} alt="" draggable="false" @error=${hideBrokenImage}>`) : nothing}
  </span>`;
}

function hueOf(text) {
  let hash = 0;
  for (const char of String(text)) {
    hash = (hash * 31 + char.codePointAt(0)) | 0;
  }

  return Math.abs(hash) % 360;
}

// Blurred cover behind the now-playing area.
export function backdrop(url) {
  const src = assetUrl(url);
  return src
    ? keyed(src, html`<span class="backdrop" aria-hidden="true" style=${`background-image:url("${src.replace(/"/g, '%22')}")`}></span>`)
    : html`<span class="backdrop empty" aria-hidden="true"></span>`;
}

export const spinner =(className = '') => html`<span class="spinner ${className}" aria-hidden="true"></span>`;

export function iconButton({ icon, label, onClick, disabled = false, active = false, className = '', title }) {
  return html`<button type="button" class="icon-btn ${active ? 'active' : ''} ${className}" title=${title ?? label}
    aria-label=${label} aria-pressed=${active ? 'true' : nothing} ?disabled=${disabled} @click=${onClick}>${icon}</button>`;
}

// Range input with a filled track (CSS variable --p).
export function slider({ value, min = 0, max = 100, step = 1, label, disabled = false, onInput, title, ticks = false }) {
  const safe = Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : min;
  const percent = max > min ? ((safe - min) / (max - min)) * 100 : 0;
  return html`<input type="range" class="slider ${ticks ? 'ticks' : ''}" min=${min} max=${max} step=${step}
    .value=${live(String(safe))} style=${`--p:${percent}%`} aria-label=${label} title=${title ?? nothing}
    ?disabled=${disabled} @input=${(e) => onInput(Number(e.target.value))}>`;
}

export function chip({ label, checked, onToggle, disabled = false, leading = nothing, title }) {
  return html`<button type="button" class="chip ${checked ? 'checked' : ''}" aria-pressed=${checked ? 'true' : 'false'}
    ?disabled=${disabled} title=${title ?? nothing} @click=${onToggle}>
    ${checked ? html`<span class="chip-check">${icons.check}</span>` : nothing}${leading}<span>${label}</span>
  </button>`;
}

export function checkbox({ label, checked, onChange, disabled = false, title }) {
  return html`<label class="checkbox ${disabled ? 'disabled' : ''}" title=${title ?? nothing}>
    <input type="checkbox" .checked=${live(Boolean(checked))} ?disabled=${disabled} @change=${(e) => onChange(e.target.checked)}>
    <span>${label}</span>
  </label>`;
}

export function cardHeader(title, hint, actions = nothing) {
  return html`<div class="card-header">
    <div>
      <h2 class="section-title">${title}</h2>
      ${hint ? html`<p class="hint">${hint}</p>` : nothing}
    </div>
    ${actions !== nothing ? html`<div class="card-actions">${actions}</div>` : nothing}
  </div>`;
}

export function expander({ id, title, hint, expanded, onToggle, body }) {
  return html`<section class="card expander ${expanded ? 'open' : ''}">
    <button type="button" class="expander-head" aria-expanded=${expanded ? 'true' : 'false'} aria-controls=${id} @click=${onToggle}>
      <span>
        <span class="section-title">${title}</span>
        ${hint ? html`<span class="hint">${hint}</span>` : nothing}
      </span>
      <span class="chevron">${icons.chevronDown}</span>
    </button>
    ${expanded ? html`<div class="expander-body" id=${id}>${body()}</div>` : nothing}
  </section>`;
}

export function playPauseButton({ playing, buffering, disabled, label, title, onClick }) {
  return html`<span class="play-wrap">
    <button type="button" class="play-btn" aria-label=${label} title=${title} ?disabled=${disabled} @click=${onClick}>
      ${playing ? icons.pause : icons.play}
    </button>
    ${buffering ? spinner('ring') : nothing}
  </span>`;
}

// Enter/Space on non-button elements with a click handler.
export function activateOnKey(handler) {
  return (event) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      handler(event);
    }
  };
}
