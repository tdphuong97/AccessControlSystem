// App shell: header, tabs with device dots, update bar, and the screen host. Screens register themselves in
// ACS.screens[name] = { render(root, params) -> optional cleanup function }.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  ACS.screens = ACS.screens || {};
  ACS.state = null;
  const listeners = [];
  ACS.onState = (fn) => { listeners.push(fn); return () => { const i = listeners.indexOf(fn); if (i >= 0) listeners.splice(i, 1); }; };
  let updateDismissed = false;
  let mounter = null;
  let current = { name: 'people', params: {} };

  const MAIN_TAB = ['people', 'person', 'new/info', 'new/face', 'new/finger', 'new/done'];

  // "Máy tầng trệt" -> "Tầng trệt"
  function shortName(name) {
    const s = String(name || '').replace(/^Máy\s+/i, '');
    return s.charAt(0).toLocaleUpperCase('vi') + s.slice(1);
  }

  const DOT = { ok: 'var(--ok-dot)', wait: 'var(--wait-dot)', err: 'var(--err)' };
  function deviceDot(d) {
    const st = ACS.recon.deviceStatus(d);
    return '<span class="dev-dot"><span class="dot" style="background:' + DOT[st.tone] + '"></span>' + esc(shortName(d.name)) + ': ' + st.short + '</span>';
  }

  function shell() {
    document.getElementById('app').innerHTML =
      '<header class="strip">' + ACS.icons.LOGO + '<span class="strip-name">Access Control System</span>' +
      '<span class="strip-sub">Đăng ký khuôn mặt &amp; vân tay</span></header>' +
      '<div class="tabrow"><nav class="tabs" aria-label="Mục chính">' +
      '<a class="tab" data-tab="main" href="#/people">' + icon('people', 18) + 'Người dùng</a>' +
      '<a class="tab" data-tab="devices" href="#/settings">' + icon('device', 18) + 'Máy chấm công &amp; cập nhật</a></nav>' +
      '<div class="devdots" id="devdots"></div></div>' +
      '<main id="screen" class="screen"></main>';
    drawChrome();
  }

  function drawChrome() {
    const st = ACS.state;
    const dots = document.getElementById('devdots');
    if (dots && st) dots.innerHTML = st.devices.map(deviceDot).join('');
    const main = MAIN_TAB.includes(current.name);
    document.querySelectorAll('.tab').forEach((t) => {
      const active = (t.dataset.tab === 'main') === main;
      t.classList.toggle('on', active);
      if (active) t.setAttribute('aria-current', 'page'); else t.removeAttribute('aria-current');
    });
  }

  // Shared "new version" bar; each screen that wants it calls ACS.updateBar().
  ACS.updateBar = function () {
    const u = ACS.state && ACS.state.update;
    if (!u || updateDismissed) return '';
    return '<div class="updbar"><span class="updbar-ic">' + icon('download', 20) + '</span>' +
      '<div class="updbar-txt"><div class="fw6">Đã có bản mới ' + esc(u.version) + '</div>' +
      '<div class="updbar-note">' + esc(u.notes) + '</div></div>' +
      '<div class="updbar-btns"><a class="btn btn-primary" href="#/settings">Cập nhật ngay</a>' +
      '<button type="button" class="btn" data-act="dismiss-update">Để sau</button></div></div>';
  };
  ACS.dismissUpdate = () => { updateDismissed = true; };

  function mountScreen(route) {
    current = route;
    drawChrome();
    const screenEl = document.getElementById('screen');
    if (!mounter) mounter = ACS.mount.createMounter(screenEl, () => document.createElement('div'));
    window.scrollTo(0, 0);
    const screen = ACS.screens[route.name];
    if (!screen) {
      mounter.mount((host) => { host.innerHTML = '<div class="pad"><div class="card empty"><h1 class="h1">Màn hình này sẽ có ở bước sau</h1>' +
        '<p class="muted">Bạn có thể quay lại danh sách người dùng.</p><a class="btn btn-primary" href="#/people">Về danh sách</a></div></div>'; });
      return;
    }
    mounter.mount((host, ctx) => screen.render(host, route.params, ctx));
  }

  function setState(st) {
    ACS.state = st;
    drawChrome();
    listeners.slice().forEach((fn) => fn(st));
  }

  ACS.setUpdate = (update) => { updateDismissed = false; setState(Object.assign({}, ACS.state, { update })); };
  ACS.refreshState = async function () { setState(await ACS.bridge.call('app.state')); };

  async function boot() {
    shell();
    try {
      setState(await ACS.bridge.call('app.state'));
    } catch (e) {
      document.getElementById('screen').innerHTML = '<div class="pad"><div class="alert alert-err">' + esc(ACS.ui.errMessage(e)) + '</div></div>';
      return;
    }
    ACS.bridge.on('devices.changed', (devices) => setState(Object.assign({}, ACS.state, { devices })));
    ACS.bridge.on('update.available', (update) => { updateDismissed = false; setState(Object.assign({}, ACS.state, { update })); });
    ACS.router.start((route) => {
      if (ACS.state.firstRun && route.name === 'people' && ACS.screens.setup) { ACS.router.go('setup'); return; }
      mountScreen(route);
    });
  }

  on(document, 'click', '[data-act="dismiss-update"]', () => { ACS.dismissUpdate(); setState(ACS.state); });
  boot();
})();
