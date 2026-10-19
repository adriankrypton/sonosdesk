'use strict';

const crypto = require('node:crypto');
const fs = require('node:fs');
const path = require('node:path');
const { app, BrowserWindow, Menu, dialog, ipcMain, nativeTheme, safeStorage, session, shell } = require('electron');
const { ServerProcess } = require('./lib/server');
const { loadOrCreateSecretKey } = require('./lib/secret');
const { Prefs } = require('./lib/prefs');
const { MainI18n, resolveLanguage } = require('./lib/i18n');
const { registerScheme, installHandler, ORIGIN } = require('./lib/protocol');
const { TrayController } = require('./lib/tray');
const { registerHotkeys, unregisterHotkeys } = require('./lib/hotkeys');

const isMac = process.platform === 'darwin';
const RENDERER_DIR = path.join(__dirname, 'renderer');
const PACKAGED_TIMEOUT_MS = 30_000;
// "dotnet run" compiles first, which can take longer than starting the published server.
const DEV_BUILD_TIMEOUT_MS = 120_000;

// Chromium's caches stay out of the folder the server keeps its settings in.
const dataRoot = process.platform === 'win32' ? (process.env.LOCALAPPDATA || app.getPath('appData')) : app.getPath('appData');
app.setPath('userData', path.join(dataRoot, 'SonosDesk', 'Electron'));
registerScheme();

let mainWindow = null;
let tray = null;
let server = null;
let serverInfo = null;
let quitting = false;
let serverStopped = false;
let prefs = null;
let hotkeySignature = null;
const i18n = new MainI18n();

function log(message) {
  try {
    const dir = app.getPath('logs');
    fs.mkdirSync(dir, { recursive: true });
    fs.appendFileSync(path.join(dir, 'main.log'), `${new Date().toISOString()} ${message}\n`);
  } catch {
    // Logging must never break the app.
  }
}

const logger = { warn: (message) => log(`WARN ${message}`) };

function backgroundColor() {
  return nativeTheme.shouldUseDarkColors ? '#202020' : '#f3f3f3';
}

function isTrustedSender(event) {
  return Boolean(mainWindow)
    && event.sender === mainWindow.webContents
    && typeof event.senderFrame?.url === 'string'
    && event.senderFrame.url.startsWith(`${ORIGIN}/`);
}

function sendCommand(command) {
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.webContents.send('tray:command', command);
  }
}

function showWindow(page) {
  if (!mainWindow || mainWindow.isDestroyed()) {
    createWindow(true);
  }

  if (mainWindow.isMinimized()) {
    mainWindow.restore();
  }

  mainWindow.show();
  mainWindow.focus();
  if (page) {
    sendCommand({ type: 'open', page });
  }
}

function quit() {
  quitting = true;
  app.quit();
}

function createWindow(show) {
  mainWindow = new BrowserWindow({
    width: 1120,
    height: 740,
    minWidth: 900,
    minHeight: 600,
    show: false,
    title: 'SonosDesk',
    backgroundColor: backgroundColor(),
    icon: isMac ? undefined : path.join(__dirname, 'assets', 'icon.ico'),
    titleBarStyle: isMac ? 'hiddenInset' : 'default',
    autoHideMenuBar: true,
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: true,
      // Tray, hotkeys and the event stream keep working while the window is hidden.
      backgroundThrottling: false,
      spellcheck: false,
      devTools: !app.isPackaged,
    },
  });

  mainWindow.once('ready-to-show', () => {
    if (show) {
      mainWindow.show();
    }
  });

  mainWindow.on('close', (event) => {
    if (quitting) {
      return;
    }

    if (isMac || prefs.get('closeToTray') !== false) {
      event.preventDefault();
      mainWindow.hide();
    } else {
      quitting = true;
    }
  });

  mainWindow.on('closed', () => {
    mainWindow = null;
  });

  const contents = mainWindow.webContents;
  contents.on('will-navigate', (event, url) => {
    if (!url.startsWith(`${ORIGIN}/`)) {
      event.preventDefault();
    }
  });
  contents.setWindowOpenHandler(({ url }) => {
    openExternalSafe(url);
    return { action: 'deny' };
  });

  mainWindow.loadURL(serverInfo ? `${ORIGIN}/index.html` : `${ORIGIN}/loading.html`);
}

function openExternalSafe(url) {
  try {
    const parsed = new URL(url);
    if (parsed.protocol === 'https:') {
      shell.openExternal(parsed.toString());
      return true;
    }
  } catch {
    // Not a URL.
  }

  return false;
}

function serverCommand() {
  const exeName = process.platform === 'win32' ? 'SonosDesk.Server.exe' : 'SonosDesk.Server';
  if (app.isPackaged) {
    const exe = path.join(process.resourcesPath, 'server', exeName);
    return { command: exe, args: [], shell: false, cwd: path.dirname(exe), timeoutMs: PACKAGED_TIMEOUT_MS };
  }

  const custom = process.env.SONOSDESK_SERVER_CMD;
  if (custom && custom.trim()) {
    return { command: custom, args: [], shell: true, cwd: __dirname, timeoutMs: DEV_BUILD_TIMEOUT_MS };
  }

  const project = path.join(__dirname, '..', 'src', 'SonosDesk.Server');
  const built = path.join(project, 'bin', 'Debug', 'net10.0', exeName);
  if (fs.existsSync(built)) {
    return { command: built, args: [], shell: false, cwd: path.dirname(built), timeoutMs: PACKAGED_TIMEOUT_MS };
  }

  return {
    command: 'dotnet',
    args: ['run', '--project', project, '-c', 'Debug', '--no-launch-profile'],
    shell: false,
    cwd: path.join(__dirname, '..'),
    timeoutMs: DEV_BUILD_TIMEOUT_MS,
  };
}

async function startServer() {
  serverInfo = null;
  const token = crypto.randomBytes(32).toString('hex');
  const secret = loadOrCreateSecretKey({ safeStorage, userDataDir: app.getPath('userData'), logger });
  const env = { ...process.env, SONOSDESK_TOKEN: token, SONOSDESK_SECRET_KEY: secret.key };
  delete env.SONOSDESK_SERVER_CMD;
  // Server messages in the language the UI will show (the system language when none is set).
  env.SONOSDESK_LANGUAGE = resolveLanguage(prefs.get('language'), app.getLocale());

  const { command, args, shell: useShell, cwd, timeoutMs } = serverCommand();
  const logFile = path.join(app.getPath('logs'), 'server.log');
  server = new ServerProcess({ command, args, shell: useShell, cwd, env, logFile, timeoutMs });
  server.onUnexpectedExit = (code) => onServerLost(code);
  serverStopped = false;

  try {
    const baseUrl = await server.start();
    serverInfo = { baseUrl, token };
    log(`server ready at ${baseUrl}`);
    if (mainWindow && !mainWindow.isDestroyed()) {
      mainWindow.loadURL(`${ORIGIN}/index.html`);
    } else {
      createWindow(!prefs.get('startMinimized'));
    }
  } catch (error) {
    log(`server start failed: ${error.message}`);
    await server.stop();
    server.closeLog();
    serverStopped = true;
    if (!quitting) {
      await showStartupError(error, logFile);
    }
  }
}

async function showStartupError(error, logFile) {
  const reason = error.kind === 'timeout'
    ? i18n.t('electron.startup.timeout', error.detail)
    : error.kind === 'exited'
      ? i18n.t('electron.startup.exited', error.detail)
      : i18n.t('electron.startup.spawn_failed', error.detail);
  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.show();
  }

  for (;;) {
    const options = {
      type: 'error',
      title: 'SonosDesk',
      message: i18n.t('electron.startup.title'),
      detail: `${reason}\n\n${i18n.t('electron.startup.log_hint', logFile)}`,
      buttons: [i18n.t('electron.startup.retry'), i18n.t('electron.startup.open_logs'), i18n.t('electron.startup.quit')],
      defaultId: 0,
      cancelId: 2,
      noLink: true,
    };
    const { response } = mainWindow && !mainWindow.isDestroyed()
      ? await dialog.showMessageBox(mainWindow, options)
      : await dialog.showMessageBox(options);
    if (response === 0) {
      startServer();
      return;
    }

    if (response === 1) {
      await shell.openPath(app.getPath('logs'));
      continue;
    }

    quit();
    return;
  }
}

async function onServerLost(code) {
  log(`server exited unexpectedly (${code})`);
  server?.closeLog();
  serverInfo = null;
  serverStopped = true;
  if (quitting) {
    return;
  }

  if (mainWindow && !mainWindow.isDestroyed()) {
    mainWindow.loadURL(`${ORIGIN}/loading.html`);
    mainWindow.show();
  }

  const options = {
    type: 'error',
    title: 'SonosDesk',
    message: i18n.t('electron.server.stopped'),
    detail: i18n.t('electron.startup.log_hint', path.join(app.getPath('logs'), 'server.log')),
    buttons: [i18n.t('electron.server.restart'), i18n.t('electron.startup.quit')],
    defaultId: 0,
    cancelId: 1,
    noLink: true,
  };
  const { response } = mainWindow ? await dialog.showMessageBox(mainWindow, options) : await dialog.showMessageBox(options);
  if (response === 0) {
    startServer();
  } else {
    quit();
  }
}

function applySettings(settings, language) {
  if (!settings || typeof settings !== 'object') {
    return;
  }

  const theme = ['light', 'dark'].includes(settings.theme) ? settings.theme : 'system';
  prefs.update({
    language: typeof settings.language === 'string' ? settings.language : null,
    theme,
    startMinimized: Boolean(settings.startMinimized),
    closeToTray: settings.closeToTray !== false,
  });

  if (nativeTheme.themeSource !== theme) {
    nativeTheme.themeSource = theme;
  }

  if (typeof language === 'string') {
    i18n.setLanguage(resolveLanguage(language, app.getLocale()));
    tray?.rebuild();
    setupAppMenu();
  }

  if (app.isPackaged) {
    try {
      app.setLoginItemSettings({ openAtLogin: Boolean(settings.startWithWindows), openAsHidden: isMac && Boolean(settings.startMinimized) });
    } catch (error) {
      log(`login item could not be set: ${error.message}`);
    }
  }

  const signature = JSON.stringify([settings.hotkeys ?? null, settings.windowsMediaControls !== false]);
  if (signature !== hotkeySignature) {
    hotkeySignature = signature;
    const failed = registerHotkeys(settings, sendCommand);
    if (failed.length > 0) {
      sendCommand({ type: 'hotkeysFailed', keys: failed });
    }
  }
}

function text(value, max) {
  return typeof value === 'string' ? value.slice(0, max) : '';
}

// The renderer is trusted, but the tray only takes what it needs, in bounded sizes.
function sanitizeTrayState(state) {
  if (!state || typeof state !== 'object') {
    return null;
  }

  const target = state.target && typeof state.target === 'object'
    ? { kind: text(state.target.kind, 16), id: text(state.target.id, 200), name: text(state.target.name, 200) }
    : null;
  const list = (items, map) => (Array.isArray(items) ? items.slice(0, 100).map(map) : []);
  return {
    target,
    title: state.title ? text(state.title, 300) : null,
    playing: Boolean(state.playing),
    buffering: Boolean(state.buffering),
    muted: Boolean(state.muted),
    canNext: state.canNext !== false,
    canPrevious: state.canPrevious !== false,
    canPlayStations: state.canPlayStations !== false,
    volumeStep: Number.isInteger(state.volumeStep) ? state.volumeStep : 5,
    rooms: list(state.rooms, (r) => ({ kind: text(r?.kind, 16), id: text(r?.id, 200), name: text(r?.name, 200), active: Boolean(r?.active) })),
    stations: list(state.stations, (s) => ({ id: text(s?.id, 300), name: text(s?.name, 200) })),
  };
}

function setupIpc() {
  ipcMain.handle('server:get', (event) => (isTrustedSender(event) && serverInfo ? { ...serverInfo } : null));
  ipcMain.handle('app:info', (event) => (isTrustedSender(event)
    ? { version: app.getVersion(), platform: process.platform, arch: process.arch, packaged: app.isPackaged }
    : null));
  ipcMain.handle('shell:open-external', (event, url) => isTrustedSender(event) && openExternalSafe(String(url)));
  ipcMain.handle('app:open-logs', async (event) => {
    if (isTrustedSender(event)) {
      await shell.openPath(app.getPath('logs'));
    }
  });
  ipcMain.on('tray:state', (event, state) => {
    if (isTrustedSender(event)) {
      tray?.update(sanitizeTrayState(state));
    }
  });
  ipcMain.on('settings:changed', (event, payload) => {
    if (isTrustedSender(event)) {
      applySettings(payload?.settings, payload?.language);
    }
  });
}

function setupAppMenu() {
  if (!isMac) {
    Menu.setApplicationMenu(null);
    return;
  }

  Menu.setApplicationMenu(Menu.buildFromTemplate([
    {
      label: app.name,
      submenu: [
        { role: 'about' },
        { type: 'separator' },
        { label: i18n.t('main.settings'), accelerator: 'Command+,', click: () => showWindow('settings') },
        { type: 'separator' },
        { role: 'services' },
        { type: 'separator' },
        { role: 'hide' },
        { role: 'hideOthers' },
        { role: 'unhide' },
        { type: 'separator' },
        { label: i18n.t('tray.exit'), accelerator: 'Command+Q', click: () => quit() },
      ],
    },
    { role: 'editMenu' },
    ...(app.isPackaged ? [] : [{ role: 'viewMenu' }]),
    { role: 'windowMenu' },
  ]));
}

function restrictPermissions() {
  session.defaultSession.setPermissionRequestHandler((_contents, permission, callback) => {
    callback(permission === 'clipboard-sanitized-write');
  });
  session.defaultSession.setPermissionCheckHandler((_contents, permission) => permission === 'clipboard-sanitized-write');
}

if (!app.requestSingleInstanceLock()) {
  app.quit();
} else {
  app.on('second-instance', () => {
    if (prefs) {
      showWindow();
    }
  });

  if (process.platform === 'win32') {
    app.setAppUserModelId('io.github.adriankrypton.sonosdesk');
  }

  app.whenReady().then(() => {
    prefs = new Prefs(app.getPath('userData'));
    i18n.setLanguage(resolveLanguage(prefs.get('language'), app.getLocale()));
    const theme = prefs.get('theme');
    nativeTheme.themeSource = ['light', 'dark'].includes(theme) ? theme : 'system';
    nativeTheme.on('updated', () => mainWindow?.setBackgroundColor(backgroundColor()));
    restrictPermissions();
    installHandler({ rendererDir: RENDERER_DIR, getServer: () => serverInfo });
    setupIpc();
    setupAppMenu();

    tray = new TrayController({
      icon: path.join(__dirname, 'assets', isMac ? 'trayTemplate.png' : 'icon.ico'),
      isMac,
      i18n,
      onCommand: sendCommand,
      onOpen: (page) => showWindow(page),
      onQuit: () => quit(),
    });
    tray.create();

    createWindow(!prefs.get('startMinimized'));
    startServer();
  });

  app.on('activate', () => {
    if (app.isReady() && prefs) {
      showWindow();
    }
  });

  app.on('before-quit', () => {
    quitting = true;
  });

  app.on('window-all-closed', () => {
    if (!isMac) {
      quit();
    }
  });

  app.on('will-quit', (event) => {
    unregisterHotkeys();
    if (server && !serverStopped) {
      event.preventDefault();
      server.stop().finally(() => {
        serverStopped = true;
        server.closeLog();
        tray?.destroy();
        app.quit();
      });
    }
  });
}
