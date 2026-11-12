(() => {
  const webview = document.getElementById('portalView');
  const menuScreen = document.getElementById('menuScreen');
  const webviewScreen = document.getElementById('webviewScreen');
  const shortcutList = document.getElementById('shortcutList');
  const userNameEl = document.getElementById('userName');
  const backBtn = document.getElementById('backBtn');
  const mainTitle = document.getElementById('mainTitle');
  const subTitle = document.getElementById('subTitle');
  
  let appConfig = { portalUrl: '', shortcuts: [] };

  window.api.getCurrentUser().then(u => {
    userNameEl.textContent = u.domain ? `${u.domain}\\${u.username}` : u.username;
  });

  // CSS-ОЧИСТКА ДЛЯ JIRA
  const JIRA_CLEANUP_CSS = `
    #header, #jira-header, .aui-header, .aui-navgroup-horizontal, #announcement-banner,
    .aui-sidebar, .aui-page-panel-nav, .iaw-menu, #footer, .aui-footer, .breadcrumbs,
    .sd-portal__header, .sd-portal__footer, .customer-portal__header, .customer-portal__footer,
    .aui-page-header, .aui-message, .aui-page-header-actions, .aui-page-panel-footer, .jira-footer,
    [data-testid="portal-footer"], .sd-portal__hero, .sd-portal__welcome, .sd-portal__search, 
    .portal-hero, .portal-banner { display: none !important; }

    html, body { background: #ffffff !important; margin: 0 !important; padding: 0 !important; }
    body, #content, #main, .aui-page-panel, .aui-page-panel-inner, .aui-page-panel-content,
    .aui-page-panel-content-primary, .sd-portal, .customer-portal, form.aui, .form-body {
      margin: 0 !important; padding: 10px !important; border: none !important;
      box-shadow: none !important; background: transparent !important;
    }

    h1, h2, h3 { font-size: 15px !important; font-weight: 700 !important; color: #0f172a !important; margin-bottom: 12px !important; }
    .field-group { margin-bottom: 12px !important; }
    label { font-size: 11px !important; font-weight: 600 !important; color: #1e293b !important; margin-bottom: 4px !important; display: block !important; }
    input[type="text"], input[type="email"], select, textarea {
      width: 100% !important; padding: 7px 10px !important; font-size: 12px !important; font-family: inherit !important;
      border: 1.5px solid #e2e8f0 !important; border-radius: 8px !important; background: #f8fafc !important;
      color: #0f172a !important; outline: none !important; box-sizing: border-box !important;
    }
    input:focus, select:focus, textarea:focus { border-color: #0097D2 !important; background: #ffffff !important; }
    .aui-button, button {
      width: 100% !important; background: linear-gradient(135deg, #0047BB, #0097D2) !important; color: white !important;
      border: none !important; border-radius: 8px !important; padding: 9px !important; font-weight: 700 !important;
      font-size: 13px !important; cursor: pointer !important; box-shadow: 0 4px 10px rgba(0, 71, 187, 0.22) !important; margin-top: 5px !important;
    }
    ::-webkit-scrollbar { display: none !important; }
  `;

  function injectCleanup() { try { webview.insertCSS(JIRA_CLEANUP_CSS); } catch (_) {} }

  function openForm(url) {
    if (!url) return;
    menuScreen.style.display = 'none';
    webviewScreen.style.display = 'flex';
    backBtn.style.display = 'inline-flex';
    mainTitle.textContent = 'Создание заявки';
    subTitle.textContent = 'Заполните поля и отправьте';
    try { webview.loadURL(url); } catch (_) { webview.setAttribute('src', url); }
  }

  function showMenu() {
    menuScreen.style.display = 'block';
    webviewScreen.style.display = 'none';
    backBtn.style.display = 'none';
    mainTitle.textContent = 'Техническая поддержка';
    subTitle.textContent = 'Выберите тип заявки';
  }

  document.getElementById('closeBtn').addEventListener('click', () => window.api.hideWindow());
  backBtn.addEventListener('click', showMenu);

  webview.addEventListener('did-finish-load', injectCleanup);
  webview.addEventListener('dom-ready', injectCleanup);

  window.api.getAppConfig().then(cfg => {
    appConfig = cfg;
    shortcutList.innerHTML = '';
    
    const items = appConfig.shortcuts.length ? appConfig.shortcuts : [
      { label: 'Открыть портал', sub: 'Все типы заявок', icon: 'portal', url: appConfig.portalUrl }
    ];

    items.forEach(sc => {
      const btn = document.createElement('button');
      btn.className = 'shortcut-item';
      btn.innerHTML = `
        <div class="shortcut-icon">${getIcon(sc.icon)}</div>
        <div class="shortcut-text">
          <div class="shortcut-title">${sc.label}</div>
          <div class="shortcut-sub">${sc.sub || 'Создать заявку'}</div>
        </div>
        <div class="shortcut-arrow"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="9 18 15 12 9 6"/></svg></div>
      `;
      btn.addEventListener('click', () => openForm(sc.url));
      shortcutList.appendChild(btn);
    });
  });

  function getIcon(name) {
    if (name === 'alert') return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/><line x1="12" y1="9" x2="12" y2="13"/><line x1="12" y1="17" x2="12.01" y2="17"/></svg>`;
    if (name === 'key') return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"/></svg>`;
    return `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>`;
  }
})();