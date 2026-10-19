import { html, svg } from '../../vendor/lit-html/lit-html.js';

// Line icons on a 24 × 24 grid (in the spirit of Fluent System Icons, drawn for SonosDesk).
const stroke = (content) => html`<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"
  fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">${content}</svg>`;
const filled = (content) => html`<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"
  fill="currentColor">${content}</svg>`;

function gearPath() {
  const teeth = 8;
  const outer = 9;
  const inner = 7.1;
  const points = [];
  for (let i = 0; i < teeth * 4; i++) {
    const angle = (Math.PI * 2 * i) / (teeth * 4) - Math.PI / 2;
    const r = i % 4 === 0 || i % 4 === 1 ? outer : inner;
    points.push(`${(12 + r * Math.cos(angle)).toFixed(2)} ${(12 + r * Math.sin(angle)).toFixed(2)}`);
  }

  return `M${points.join('L')}Z`;
}

const GEAR = gearPath();

export const icons = {
  refresh: stroke(svg`<path d="M19.5 12a7.5 7.5 0 1 1-2.2-5.3"/><path d="M19.5 4.5v4h-4"/>`),
  speaker: stroke(svg`<rect x="6" y="2.75" width="12" height="18.5" rx="3"/><circle cx="12" cy="14.6" r="3.2"/><circle cx="12" cy="7.4" r="1" fill="currentColor"/>`),
  speakers: stroke(svg`<rect x="2.75" y="5" width="8" height="14" rx="2"/><rect x="13.25" y="5" width="8" height="14" rx="2"/><circle cx="6.75" cy="14" r="2"/><circle cx="17.25" cy="14" r="2"/>`),
  link: stroke(svg`<path d="M10 14a4 4 0 0 0 5.66 0l3-3a4 4 0 0 0-5.66-5.66l-1 1"/><path d="M14 10a4 4 0 0 0-5.66 0l-3 3a4 4 0 0 0 5.66 5.66l1-1"/>`),
  play: filled(svg`<path d="M8 5.2v13.6a1 1 0 0 0 1.53.85l10.6-6.8a1 1 0 0 0 0-1.7L9.53 4.35A1 1 0 0 0 8 5.2z"/>`),
  pause: filled(svg`<rect x="6.5" y="4.75" width="3.75" height="14.5" rx="1.2"/><rect x="13.75" y="4.75" width="3.75" height="14.5" rx="1.2"/>`),
  pauseOutline: stroke(svg`<rect x="6.5" y="5" width="3.5" height="14" rx="1"/><rect x="14" y="5" width="3.5" height="14" rx="1"/>`),
  previous: filled(svg`<rect x="5" y="5.5" width="2.2" height="13" rx="1.1"/><path d="M19 6.4v11.2a1 1 0 0 1-1.55.83l-8.2-5.6a1 1 0 0 1 0-1.66l8.2-5.6A1 1 0 0 1 19 6.4z"/>`),
  next: filled(svg`<rect x="16.8" y="5.5" width="2.2" height="13" rx="1.1"/><path d="M5 6.4v11.2a1 1 0 0 0 1.55.83l8.2-5.6a1 1 0 0 0 0-1.66l-8.2-5.6A1 1 0 0 0 5 6.4z"/>`),
  shuffle: stroke(svg`<path d="M3 17h3.2c2.1 0 3.3-1.1 4.4-2.9l2.1-3.4C13.8 8.9 15 7.8 17.1 7.8H21"/><path d="M3 7.8h3.2c1.5 0 2.5.6 3.4 1.6"/><path d="M14.4 15.4c.9 1 1.9 1.6 3.4 1.6H21"/><path d="M18.5 5.3 21 7.8l-2.5 2.5"/><path d="M18.5 14.5 21 17l-2.5 2.5"/>`),
  repeat: stroke(svg`<path d="M4 11.5V9.5a3 3 0 0 1 3-3h13"/><path d="M17 3.5l3 3-3 3"/><path d="M20 12.5v2a3 3 0 0 1-3 3H4"/><path d="M7 20.5l-3-3 3-3"/>`),
  repeatOne: stroke(svg`<path d="M4 11.5V9.5a3 3 0 0 1 3-3h13"/><path d="M17 3.5l3 3-3 3"/><path d="M20 12.5v2a3 3 0 0 1-3 3H4"/><path d="M7 20.5l-3-3 3-3"/><path d="M11.2 10.6l1.3-.9v4.6"/>`),
  crossfade: stroke(svg`<path d="M3 17.5c6.5 0 8.5-11 18-11"/><path d="M3 6.5c6.5 0 8.5 11 18 11"/>`),
  moon: stroke(svg`<path d="M19.5 14.6A8 8 0 1 1 9.4 4.5a6.4 6.4 0 0 0 10.1 10.1z"/>`),
  volume: stroke(svg`<path d="M4 9.5h3.2L12 5.5v13l-4.8-4H4z"/><path d="M15.5 9a4.2 4.2 0 0 1 0 6"/><path d="M18 6.5a7.6 7.6 0 0 1 0 11"/>`),
  mute: stroke(svg`<path d="M4 9.5h3.2L12 5.5v13l-4.8-4H4z"/><path d="M15.5 9.5l5 5"/><path d="M20.5 9.5l-5 5"/>`),
  music: stroke(svg`<path d="M9 17.5V6.2l11-2.2v11.5"/><circle cx="6.5" cy="17.5" r="2.5"/><circle cx="17.5" cy="15.5" r="2.5"/>`),
  settings: stroke(svg`<path d=${GEAR}/><circle cx="12" cy="12" r="3"/>`),
  info: stroke(svg`<circle cx="12" cy="12" r="9"/><path d="M12 11v5.5"/><circle cx="12" cy="7.8" r=".9" fill="currentColor" stroke="none"/>`),
  power: stroke(svg`<path d="M12 3.5v8"/><path d="M7.4 6.3a7.2 7.2 0 1 0 9.2 0"/>`),
  trash: stroke(svg`<path d="M4 7h16"/><path d="M9.5 7V4.5h5V7"/><path d="M6.5 7l1 13h9l1-13"/><path d="M10 11v5.5M14 11v5.5"/>`),
  close: stroke(svg`<path d="M6.5 6.5l11 11M17.5 6.5l-11 11"/>`),
  star: stroke(svg`<path d="M12 3.6l2.6 5.3 5.8.8-4.2 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z"/>`),
  starFilled: filled(svg`<path d="M12 3.6l2.6 5.3 5.8.8-4.2 4.1 1 5.8-5.2-2.7-5.2 2.7 1-5.8-4.2-4.1 5.8-.8z"/>`),
  back: stroke(svg`<path d="M19 12H5"/><path d="M11 6l-6 6 6 6"/>`),
  search: stroke(svg`<circle cx="10.5" cy="10.5" r="6.5"/><path d="M15.4 15.4 20 20"/>`),
  copy: stroke(svg`<rect x="8.5" y="8.5" width="11.5" height="11.5" rx="2"/><path d="M15.5 8.5V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v7.5a2 2 0 0 0 2 2h2.5"/>`),
  chevronDown: stroke(svg`<path d="M6 9l6 6 6-6"/>`),
  check: stroke(svg`<path d="M5 12.5l4.5 4.5L19 7.5"/>`),
  add: stroke(svg`<path d="M12 5v14M5 12h14"/>`),
  edit: stroke(svg`<path d="M4 20h4L19 9a2.8 2.8 0 0 0-4-4L4 16z"/><path d="M13.5 6.5l4 4"/>`),
  image: stroke(svg`<rect x="3.5" y="4.5" width="17" height="15" rx="2"/><circle cx="9" cy="10" r="1.6"/><path d="M20.5 16l-4.5-4.5L7 19.5"/>`),
  receiver: stroke(svg`<rect x="2.5" y="7" width="19" height="10" rx="2"/><circle cx="16.5" cy="12" r="2.6"/><path d="M6 10.8h5M6 13.4h3"/>`),
  trending: stroke(svg`<path d="M3 17l6-6 4 4 8-8"/><path d="M15 7h6v6"/>`),
  folder: stroke(svg`<path d="M3 7.5a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z"/>`),
  infoBar: stroke(svg`<circle cx="12" cy="12" r="9"/><path d="M12 11v5.5"/><circle cx="12" cy="7.8" r=".9" fill="currentColor" stroke="none"/>`),
  success: stroke(svg`<circle cx="12" cy="12" r="9"/><path d="M8 12.3l2.8 2.8L16.2 9.5"/>`),
  warning: stroke(svg`<path d="M12 3.8 2.8 19.5h18.4z"/><path d="M12 10v4.4"/><circle cx="12" cy="17" r=".9" fill="currentColor" stroke="none"/>`),
  error: stroke(svg`<circle cx="12" cy="12" r="9"/><path d="M9 9l6 6M15 9l-6 6"/>`),
  download: stroke(svg`<path d="M12 4v11"/><path d="M7.5 10.5 12 15l4.5-4.5"/><path d="M5 19.5h14"/>`),
};

export function severityIcon(severity) {
  return severity === 'success' ? icons.success : severity === 'warning' ? icons.warning : severity === 'error' ? icons.error : icons.infoBar;
}
