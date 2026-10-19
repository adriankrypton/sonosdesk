'use strict';

const { Menu, Tray, nativeImage } = require('electron');

function truncate(text, max) {
  return text.length > max ? `${text.slice(0, max - 1)}…` : text;
}

// Menu bar (macOS) / notification area (Windows) icon. The renderer owns the playback state and
// pushes a compact copy here; menu actions go back to the renderer as commands.
class TrayController {
  constructor({ icon, isMac, i18n, onCommand, onOpen, onQuit }) {
    this.icon = icon;
    this.isMac = isMac;
    this.i18n = i18n;
    this.onCommand = onCommand;
    this.onOpen = onOpen;
    this.onQuit = onQuit;
    this.state = null;
    this.tray = null;
    this.menuOpen = false;
    this.pendingRebuild = false;
  }

  create() {
    const image = nativeImage.createFromPath(this.icon);
    if (this.isMac) {
      image.setTemplateImage(true);
    }

    this.tray = new Tray(image);
    this.tray.setToolTip('SonosDesk');
    if (!this.isMac) {
      this.tray.on('click', () => this.onOpen());
    }

    this.rebuild();
  }

  update(state) {
    this.state = state;
    this.rebuild();
  }

  rebuild() {
    if (!this.tray) {
      return;
    }

    // Replacing the menu while it is open would close it under the cursor.
    if (this.menuOpen) {
      this.pendingRebuild = true;
      return;
    }

    const menu = Menu.buildFromTemplate(this.template());
    menu.on('menu-will-show', () => {
      this.menuOpen = true;
    });
    menu.on('menu-will-close', () => {
      this.menuOpen = false;
      if (this.pendingRebuild) {
        this.pendingRebuild = false;
        setTimeout(() => this.rebuild(), 0);
      }
    });
    this.tray.setContextMenu(menu);
    this.tray.setToolTip(this.tooltip());
  }

  tooltip() {
    const target = this.state?.target;
    if (!target) {
      return 'SonosDesk';
    }

    const line = this.state.title ? `${target.name}: ${this.state.title}` : target.name;
    return truncate(`SonosDesk – ${line}`, 120);
  }

  template() {
    const t = (key, ...args) => this.i18n.t(key, ...args);
    const s = this.state;
    const send = (command) => () => this.onCommand(command);
    const hasTarget = Boolean(s?.target);
    const items = [];

    if (s?.target) {
      items.push({ label: truncate(s.target.name, 60), enabled: false });
      const status = s.buffering ? t('group.buffering') : s.title;
      if (status) {
        items.push({ label: truncate(status, 60), enabled: false });
      }
    } else {
      items.push({ label: t('tray.ready'), enabled: false });
    }

    items.push(
      { type: 'separator' },
      { label: s?.playing ? t('tray.pause') : t('tray.play'), enabled: hasTarget, click: send({ type: 'toggle' }) },
      { label: t('tray.next'), enabled: hasTarget && s.canNext !== false, click: send({ type: 'next' }) },
      { label: t('tray.previous'), enabled: hasTarget && s.canPrevious !== false, click: send({ type: 'previous' }) },
      { type: 'separator' },
      { label: t('tray.volume_up', s?.volumeStep ?? 5), enabled: hasTarget, click: send({ type: 'volume', direction: 1 }) },
      { label: t('tray.volume_down', s?.volumeStep ?? 5), enabled: hasTarget, click: send({ type: 'volume', direction: -1 }) },
      { label: s?.muted ? t('tray.unmute') : t('tray.mute'), enabled: hasTarget, click: send({ type: 'mute' }) },
      { type: 'separator' },
    );

    const rooms = s?.rooms ?? [];
    items.push({
      label: t('main.rooms'),
      enabled: rooms.length > 0,
      submenu: rooms.length > 0
        ? rooms.map((room) => ({
          label: truncate(room.name, 60),
          type: 'radio',
          checked: Boolean(room.active),
          click: send({ type: 'select', kind: room.kind, id: room.id }),
        }))
        : [{ label: t('np.no_rooms'), enabled: false }],
    });

    const stations = s?.stations ?? [];
    items.push({
      label: t('tray.my_stations'),
      enabled: stations.length > 0 && hasTarget && s.canPlayStations !== false,
      submenu: stations.length > 0
        ? stations.map((station) => ({ label: truncate(station.name, 60), click: send({ type: 'playStation', id: station.id }) }))
        : [{ label: t('radio.saved_title'), enabled: false }],
    });

    items.push(
      { label: t('tray.pause_all'), enabled: rooms.some((r) => r.kind === 'group'), click: send({ type: 'pauseAll' }) },
      { type: 'separator' },
      { label: t('tray.open'), click: () => this.onOpen() },
      { label: t('tray.settings'), click: () => this.onOpen('settings') },
      { type: 'separator' },
      { label: t('tray.exit'), accelerator: this.isMac ? 'Command+Q' : undefined, click: () => this.onQuit() },
    );
    return items;
  }

  destroy() {
    this.tray?.destroy();
    this.tray = null;
  }
}

module.exports = { TrayController };
