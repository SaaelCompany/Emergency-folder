const { app, BrowserWindow, ipcMain, Tray, screen, nativeImage, shell } = require('electron');
const path = require('path');
const fs = require('fs');
const zlib = require('zlib');
const { execFile } = require('child_process');
const config = require('./config');

let fabWindow = null;
let tray = null;
let dcStatus = { ok: true, reason: '' };
let checkTimer = null;
let lastDesktopOpenAt = 0;
app.isQuitting = false;

const FAB_SIZE = 100;
const PUBLIC_DESKTOP = path.win32.join(process.env.PUBLIC || 'C:\\Users\\Public', 'Desktop');
const COMMON_STARTUP = path.win32.join(
  process.env.PROGRAMDATA || 'C:\\ProgramData',
  'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Startup'
);
const SHORTCUT_NAME = 'Emergency folder.lnk';

// ---- Единственный экземпляр приложения ----
const hasSingleInstanceLock = app.requestSingleInstanceLock();
if (!hasSingleInstanceLock) {
  app.exit(0);
} else {
  app.on('second-instance', () => {
    console.log('[APP] Повторный запуск — открываю общий рабочий стол');
    if (app.isReady()) openPublicDesktop();
    else app.whenReady().then(() => openPublicDesktop());
  });
}

// ---- Генерация иконки трея ----
function makeTrayIconPNG() {
  const width = 32, height = 32;
  const rowSize = 1 + width * 4;
  const raw = Buffer.alloc(rowSize * height);
  for (let y = 0; y < height; y++) {
    raw[y * rowSize] = 0;
    for (let x = 0; x < width; x++) {
      const i = y * rowSize + 1 + x * 4;
      const cx = width / 2, cy = height / 2;
      const dx = x + 0.5 - cx, dy = y + 0.5 - cy;
      const dist = Math.sqrt(dx * dx + dy * dy);
      if (dist <= width / 2 - 1) {
        const t = x / width;
        raw[i] = 0x00;
        raw[i + 1] = Math.round(0x47 + (0x97 - 0x47) * t);
        raw[i + 2] = Math.round(0xBB + (0xD2 - 0xBB) * t);
        raw[i + 3] = 255;
      } else { raw[i + 3] = 0; }
    }
  }
  const crcTable = (() => {
    const tbl = new Uint32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
      tbl[n] = c >>> 0;
    }
    return tbl;
  })();
  function crc32(buf) {
    let crc = 0xFFFFFFFF;
    for (let i = 0; i < buf.length; i++) crc = crcTable[(crc ^ buf[i]) & 0xFF] ^ (crc >>> 8);
    return (crc ^ 0xFFFFFFFF) >>> 0;
  }
  function chunk(type, data) {
    const len = Buffer.alloc(4); len.writeUInt32BE(data.length);
    const t = Buffer.from(type, 'ascii');
    const c = Buffer.alloc(4); c.writeUInt32BE(crc32(Buffer.concat([t, data])));
    return Buffer.concat([len, t, data, c]);
  }
  const sig = Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0); ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; ihdr[9] = 6;
  const idat = zlib.deflateSync(raw);
  return Buffer.concat([sig, chunk('IHDR', ihdr), chunk('IDAT', idat), chunk('IEND', Buffer.alloc(0))]);
}

// ---- Позиция в правом нижнем углу ----
function getBottomRightBounds(size) {
  const area = screen.getPrimaryDisplay().workArea;
  return {
    x: area.x + area.width - size,
    y: area.y + area.height - size,
    width: size,
    height: size
  };
}

// ---- Проверка КД ----
function checkDomainController() {
  return new Promise((resolve) => {
    const domain = process.env.USERDNSDOMAIN;
    if (!domain) { resolve({ ok: false, reason: 'Компьютер не в домене' }); return; }
    let settled = false;
    const killTimer = setTimeout(() => {
      if (!settled) { settled = true; resolve({ ok: false, reason: 'Таймаут проверки КД' }); }
    }, 4000);
    execFile('nltest', [`/dsgetdc:${domain}`], { timeout: 4000, windowsHide: true },
      (err) => {
        if (settled) return;
        settled = true; clearTimeout(killTimer);
        if (err) resolve({ ok: false, reason: 'КД недоступен' });
        else resolve({ ok: true, domain });
      }
    );
  });
}

async function updateDcStatus() {
  dcStatus = await checkDomainController();
  console.log(`[DC] ${dcStatus.ok ? 'OK' : 'FAIL'} (${dcStatus.reason || 'Сеть в порядке'})`);
  if (fabWindow && !fabWindow.isDestroyed()) {
    if (dcStatus.ok) {
      if (fabWindow.isVisible()) fabWindow.hide();
    } else {
      if (!fabWindow.isVisible()) fabWindow.showInactive();
      fabWindow.webContents.send('dc-status', dcStatus);
    }
  }
}

function scheduleNextCheck() {
  if (checkTimer) clearTimeout(checkTimer);
  const delay = dcStatus.ok ? config.CHECK_INTERVAL_OK : config.CHECK_INTERVAL_FAIL;
  checkTimer = setTimeout(async () => {
    await updateDcStatus();
    scheduleNextCheck();
  }, delay);
}

// ---- НАСТРОЙКА АВТОЗАГРУЗКИ ----
function setupAutostart() {
  const exePath = process.execPath;
  const workDir = path.dirname(exePath);
  const lnkPath = path.join(COMMON_STARTUP, SHORTCUT_NAME);

  // 1. Если ярлык уже в общей автозагрузке — выходим
  if (fs.existsSync(lnkPath)) {
    console.log('[AUTOSTART] ✅ Ярлык уже в общей автозагрузке:', lnkPath);
    return;
  }

  // 2. Пытаемся создать ярлык в общей папке (нужны права администратора)
  try {
    const created = shell.writeShortcutLink(lnkPath, 'create', {
      target: exePath,
      cwd: workDir,
      description: 'Emergency folder — мониторинг контроллера домена',
      icon: exePath,
      iconIndex: 0
    });

    if (created && fs.existsSync(lnkPath)) {
      console.log('[AUTOSTART] ✅ Ярлык создан в общей автозагрузке:', lnkPath);
      return;
    }
    throw new Error('writeShortcutLink не смог создать ярлык');
  } catch (e) {
    console.warn('[AUTOSTART] ⚠️ Нет прав на запись в общую автозагрузку:', e.message);
  }

  // 3. Fallback: пользовательская автозагрузка через HKCU
  try {
    app.setLoginItemSettings({
      openAtLogin: true,
      path: exePath,
      args: []
    });
    console.log('[AUTOSTART] ⚠️ Использую пользовательскую автозагрузку (HKCU\\...\\Run)');
  } catch (e) {
    console.error('[AUTOSTART] ❌ Не удалось настроить автозагрузку:', e.message);
  }
}

// ---- Окно ----
function createFabWindow() {
  const initial = getBottomRightBounds(FAB_SIZE);
  fabWindow = new BrowserWindow({
    ...initial,
    frame: false, transparent: true, resizable: false, movable: false,
    skipTaskbar: true, alwaysOnTop: true, focusable: false,
    hasShadow: false, thickFrame: false, backgroundColor: '#00000000',
    show: false, title: 'DC Monitor',
    webPreferences: {
      preload: path.join(__dirname, 'preload.js'),
      contextIsolation: true, nodeIntegration: false
    }
  });
  fabWindow.loadFile('fab.html');
  fabWindow.webContents.once('did-finish-load', () => {
    fabWindow.webContents.send('dc-status', dcStatus);
    if (!dcStatus.ok) fabWindow.showInactive();
  });
  fabWindow.setIgnoreMouseEvents(true, { forward: true });
}

async function openPublicDesktop() {
  const now = Date.now();
  if (now - lastDesktopOpenAt < 800) return;
  lastDesktopOpenAt = now;
  console.log('[DESKTOP] Открываю', PUBLIC_DESKTOP);
  try {
    const err = await shell.openPath(PUBLIC_DESKTOP);
    if (err) console.error('Не удалось открыть папку:', err);
  } catch (e) {
    console.error('Не удалось открыть папку:', e);
  }
}

function createTray() {
  const icon = nativeImage.createFromBuffer(makeTrayIconPNG());
  tray = new Tray(icon);
  tray.setToolTip('Открыть общий рабочий стол');
  console.log('[TRAY] Клик по иконке открывает', PUBLIC_DESKTOP);
  const onTrayActivate = () => openPublicDesktop();
  tray.on('click', onTrayActivate);
  tray.on('right-click', onTrayActivate);
  tray.on('double-click', onTrayActivate);
}

// ---- Старт ----
if (hasSingleInstanceLock) app.whenReady().then(() => {
  // === ОБРАБОТКА ЗАПУСКА ИЗ SCCM (SESSION 0) ===
  const isSessionZero = process.env.SESSIONNAME && process.env.SESSIONNAME.toLowerCase() === 'services';
  
  if (isSessionZero) {
    console.log('[SCCM] Запущено в служебной сессии (SYSTEM). Настраиваем автозагрузку и выходим...');
    setupAutostart();
    // Даем 5 секунд на запись файлов, после чего закрываем процесс
    setTimeout(() => {
      app.isQuitting = true;
      app.quit();
    }, 5000);
    return; // Не создаем окна и трей
  }

  // === ОБЫЧНЫЙ ЗАПУСК (ПОЛЬЗОВАТЕЛЬ) ===
  setupAutostart();
  createFabWindow();
  createTray();
  
  updateDcStatus().then(scheduleNextCheck);

  screen.on('display-metrics-changed', () => {
    if (fabWindow && !fabWindow.isDestroyed()) {
      fabWindow.setBounds(getBottomRightBounds(FAB_SIZE));
    }
  });
});

app.on('before-quit', () => {
  app.isQuitting = true;
  if (checkTimer) clearTimeout(checkTimer);
});

// ---- IPC ----
ipcMain.on('set-ignore-mouse', (e, ignore) => {
  if (fabWindow && !fabWindow.isDestroyed()) fabWindow.setIgnoreMouseEvents(!!ignore, { forward: true });
});

ipcMain.on('fab-clicked', () => {
  openPublicDesktop();
  updateDcStatus().then(scheduleNextCheck);
});

ipcMain.handle('get-dc-status', () => dcStatus);