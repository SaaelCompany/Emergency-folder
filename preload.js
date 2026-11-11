const { contextBridge, ipcRenderer } = require('electron');

contextBridge.exposeInMainWorld('api', {
  setIgnoreMouse: (ignore) => ipcRenderer.send('set-ignore-mouse', ignore),
  fabClicked: () => ipcRenderer.send('fab-clicked'), // Новый метод
  getDcStatus: () => ipcRenderer.invoke('get-dc-status'),
  onDcStatus: (cb) => ipcRenderer.on('dc-status', (e, data) => cb(data))
});