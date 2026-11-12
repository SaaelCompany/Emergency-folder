const { app, BrowserWindow, ipcMain, Tray, screen, nativeImage, shell } = require('electron');
const path = require('path');
const fs = require('fs');
const { execFile, execFileSync } = require('child_process');
const config = require('./config');
const { makeFolderPNG } = require('./folder-icon');

let fabWindow = null;
let tray = null;
let dcStatus = { ok: true, reason: '' };
let checkTimer = null;
let lastDesktopOpenAt = 0;
app.isQuitting = false;

const FAB_SIZE = 100;
const APP_USER_MODEL_ID = 'ru.bzklinika.emergencyfolder';
const PUBLIC_DESKTOP = path.win32.join(process.env.PUBLIC || 'C:\\Users\\Public', 'Desktop');
const COMMON_STARTUP = path.win32.join(
  process.env.PROGRAMDATA || 'C:\\ProgramData',
  'Microsoft', 'Windows', 'Start Menu', 'Programs', 'Startup'
);
const SHORTCUT_NAME = 'Emergency folder.lnk';

function isSystemContext() {
  const session = String(process.env.SESSIONNAME || '').toLowerCase();
  if (session === 'services') return true;
  const user = String(process.env.USERNAME || '').toLowerCase();
  if (user === 'system') return true;
  const profile = String(process.env.USERPROFILE || '').toLowerCase();
  return profile.includes('\\config\\systemprofile');
}

const systemContext = isSystemContext();

function logLine(tag, message) {
  const line = `[${tag}] ${message}`;
  console.log(line);
  if (!systemContext) return;
  try {
    const dir = path.win32.join(process.env.PROGRAMDATA || 'C:\\ProgramData', 'Emergency folder');
    fs.mkdirSync(dir, { recursive: true });
    fs.appendFileSync(path.win32.join(dir, 'launch.log'), `${new Date().toISOString()} ${line}\r\n`);
  } catch (e) {
    console.error('[LOG] Не удалось записать журнал:', e.message);
  }
}

// В служебном сеансе блокировку не берём: иначе копия, запущенная у пользователя, сразу завершится.
let hasSingleInstanceLock = false;
if (!systemContext) {
  app.setAppUserModelId(APP_USER_MODEL_ID);
  hasSingleInstanceLock = app.requestSingleInstanceLock();
  if (!hasSingleInstanceLock) {
    app.exit(0);
  } else {
    app.on('second-instance', (_event, commandLine) => {
      if ((commandLine || []).some(arg => arg === '--background')) return;
      logLine('APP', 'Повторный запуск — открываю общий рабочий стол');
      if (app.isReady()) openPublicDesktop();
      else app.whenReady().then(() => openPublicDesktop());
    });
  }
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

function powershellPath() {
  const full = path.win32.join(
    process.env.SystemRoot || 'C:\\Windows',
    'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe'
  );
  return fs.existsSync(full) ? full : 'powershell.exe';
}

function shortcutMatches(lnkPath, exePath) {
  if (!fs.existsSync(lnkPath) || typeof shell.readShortcutLink !== 'function') return false;
  try {
    const link = shell.readShortcutLink(lnkPath);
    const target = path.win32.normalize(link.target || '').replace(/\\+$/, '').toLowerCase();
    return target === path.win32.normalize(exePath).replace(/\\+$/, '').toLowerCase();
  } catch (e) {
    return false;
  }
}

function writeShortcutWithShell(lnkPath, exePath) {
  if (typeof shell.writeShortcutLink !== 'function') return false;
  const operation = fs.existsSync(lnkPath) ? 'replace' : 'create';
  return shell.writeShortcutLink(lnkPath, operation, {
    target: exePath,
    cwd: path.win32.dirname(exePath),
    description: 'Emergency folder — общий рабочий стол',
    icon: exePath,
    iconIndex: 0
  }) && fs.existsSync(lnkPath);
}

function writeShortcutWithPowerShell(lnkPath, exePath) {
  if (process.platform !== 'win32') return false;
  const quote = (value) => String(value).replace(/'/g, "''");
  const script = [
    '$shell = New-Object -ComObject WScript.Shell',
    `$shortcut = $shell.CreateShortcut('${quote(lnkPath)}')`,
    `$shortcut.TargetPath = '${quote(exePath)}'`,
    `$shortcut.WorkingDirectory = '${quote(path.win32.dirname(exePath))}'`,
    "$shortcut.Description = 'Emergency folder'",
    `$shortcut.IconLocation = '${quote(exePath)},0'`,
    '$shortcut.Save()'
  ].join('; ');
  try {
    execFileSync(powershellPath(), [
      '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-Command', script
    ], { timeout: 15000, windowsHide: true });
    return fs.existsSync(lnkPath);
  } catch (e) {
    logLine('AUTOSTART', 'PowerShell не записал ярлык: ' + e.message);
    return false;
  }
}

// Ярлык в общей автозагрузке. MSI создаёт его сам; здесь запасной путь,
// если программу запустили без установщика или ярлык указывает на старый файл.
function ensureAutostart() {
  const exePath = process.execPath;
  const lnkPath = path.win32.join(COMMON_STARTUP, SHORTCUT_NAME);
  if (shortcutMatches(lnkPath, exePath)) {
    logLine('AUTOSTART', 'Ярлык уже в общей автозагрузке');
    return;
  }
  if (fs.existsSync(lnkPath)) {
    // Не добавляем вторую автозагрузку в HKCU: ярлык установщика уже запускает программу.
    try {
      if (writeShortcutWithShell(lnkPath, exePath) || writeShortcutWithPowerShell(lnkPath, exePath)) {
        logLine('AUTOSTART', 'Ярлык общей автозагрузки обновлён');
        return;
      }
    } catch (e) {
      logLine('AUTOSTART', 'Не удалось обновить ярлык: ' + e.message);
    }
    logLine('AUTOSTART', 'Ярлык общей автозагрузки уже существует');
    return;
  }
  try {
    if (writeShortcutWithShell(lnkPath, exePath)) {
      logLine('AUTOSTART', 'Ярлык записан в общую автозагрузку: ' + lnkPath);
      return;
    }
  } catch (e) {
    logLine('AUTOSTART', 'Нет прав на запись ярлыка: ' + e.message);
  }
  if (writeShortcutWithPowerShell(lnkPath, exePath)) {
    logLine('AUTOSTART', 'Ярлык записан через PowerShell: ' + lnkPath);
    return;
  }
  if (systemContext) {
    logLine('AUTOSTART', 'Общая автозагрузка не записана из служебного сеанса');
    return;
  }
  try {
    app.setLoginItemSettings({
      openAtLogin: true,
      path: exePath,
      args: []
    });
    logLine('AUTOSTART', 'Включена автозагрузка текущего пользователя');
  } catch (e) {
    logLine('AUTOSTART', 'Не удалось настроить автозагрузку: ' + e.message);
  }
}

function launchInActiveUserSessions(exePath) {
  if (process.platform !== 'win32') {
    logLine('SCCM', 'Запуск в сессии пользователя доступен только в Windows');
    return Promise.resolve();
  }
  const scriptPath = path.join(__dirname, 'launch-user.ps1');
  return new Promise((resolve) => {
    execFile(powershellPath(), [
      '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass',
      '-File', scriptPath, '-ExePath', exePath
    ], { timeout: 60000, windowsHide: true }, (err, stdout, stderr) => {
      const output = String(stdout || '').trim();
      if (output) logLine('SCCM', output);
      if (err) logLine('SCCM', 'Не удалось запустить сессию пользователя: ' + (stderr || err.message));
      else if (!output) logLine('SCCM', 'Скрипт запуска не вернул результат');
      resolve();
    });
  });
}

async function runSystemContext() {
  logLine('SCCM', 'Служебный сеанс. Настраиваю автозагрузку и запускаю программу у вошедшего пользователя.');
  ensureAutostart();
  await launchInActiveUserSessions(process.execPath);
  app.isQuitting = true;
  app.exit(0);
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
  logLine('DESKTOP', 'Открываю ' + PUBLIC_DESKTOP);
  try {
    const err = await shell.openPath(PUBLIC_DESKTOP);
    if (err) console.error('Не удалось открыть папку:', err);
  } catch (e) {
    console.error('Не удалось открыть папку:', e);
  }
}

function createTray() {
  const icon = nativeImage.createFromBuffer(makeFolderPNG(32));
  if (icon.isEmpty()) throw new Error('Пустая иконка трея');
  tray = new Tray(icon);
  tray.setToolTip('Открыть общий рабочий стол');
  logLine('TRAY', 'Клик по иконке открывает ' + PUBLIC_DESKTOP);
  const onTrayActivate = () => openPublicDesktop();
  tray.on('click', onTrayActivate);
  tray.on('right-click', onTrayActivate);
  tray.on('double-click', onTrayActivate);
}

// ---- Старт ----
if (systemContext || hasSingleInstanceLock) app.whenReady().then(() => {
  if (systemContext) {
    runSystemContext();
    return;
  }

  ensureAutostart();
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
