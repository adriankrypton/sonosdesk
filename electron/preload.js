'use strict';

const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('sonosdesk', {
  platform: process.platform,
  getServer: () => ipcRenderer.invoke('server:get'),
  getAppInfo: () => ipcRenderer.invoke('app:info'),
  openExternal: (url) => ipcRenderer.invoke('shell:open-external', String(url)),
  openLogs: () => ipcRenderer.invoke('app:open-logs'),
  setTrayState: (state) => ipcRenderer.send('tray:state', state),
  settingsChanged: (settings, language) => ipcRenderer.send('settings:changed', { settings, language }),
  onTrayCommand: (callback) => {
    const listener = (_event, command) => callback(command);
    ipcRenderer.on('tray:command', listener);
    return () => ipcRenderer.removeListener('tray:command', listener);
  },
});
