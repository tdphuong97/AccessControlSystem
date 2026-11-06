// First run (spec 5.6): add device(s) -> test -> pick source device and import -> backup password -> reconcile.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const W = ACS.wizard;
  const R = ACS.recon;
  const D = ACS.devui;
  const NAMES = ['Thêm máy', 'Kiểm tra', 'Lấy dữ liệu', 'Sao lưu', 'So khớp'];
  const spin = (n) => icon('spinner', n || 18).replace('class="ic"', 'class="ic xoay"');
  const errBox = W.errBox;
  const PW_WARNING = 'Mất mật khẩu này thì không khôi phục được bản sao lưu';

  function render(host, params, ctx) {
    const s = { step: 0, devs: [], adding: false, formErr: '', saving: false, tests: {}, source: null,
      imp: { phase: 'idle', done: 0, total: 0, name: '', summary: null, err: '' }, pwDone: false, pwErr: '', rv: null };
    const alive = () => ctx.alive();
    const okDevs = () => s.devs.filter((d) => s.tests[d.id] && s.tests[d.id].result && s.tests[d.id].result.ok);
    const srcName = () => { const d = s.devs.find((x) => x.id === s.source); return d ? d.name : 'máy'; };

    host.innerHTML = '<div class="wiz"><p class="muted">Đang tải…</p></div>';

    function foot(back, next) {
      return '<div class="wfoot">' + (back || '') + '<span class="grow"></span>' + (next || '') + '</div>';
    }
    const backBtn = (label) => '<button type="button" class="btn btn-48" data-act="su-back">' + icon('chevronLeft', 18) + (label || 'Quay lại') + '</button>';
    const nextBtn = (label, enabled) => '<button type="button" class="btn btn-primary btn-next" data-act="su-next"' + (enabled ? '' : ' disabled') + '>' + label + icon('chevronRight', 18) + '</button>';

    function savedList() {
      return s.devs.length ? '<ul class="sulist">' + s.devs.map((d) => '<li class="sulist-i"><span class="st st-ok">' + icon('checkCircle', 18) + '</span><span class="fw6">' + esc(d.name) +
        '</span><span class="mono small muted">' + esc(d.host) + '</span></li>').join('') + '</ul>' : '';
    }

    function body() {
      if (s.step === 0) {
        const showForm = s.adding || (!s.devs.length && !s.formClosed);
        return '<div><h1 class="h1 h1-md">Chào mừng! Hãy thêm máy chấm công</h1><p class="sub">Làm một lần khi mới cài, khoảng 5 phút. Nên thêm đủ cả 2 máy; thêm máy thứ hai sau cũng được.</p></div>' +
          savedList() + (!showForm && !s.devs.length ? '<div class="card empty"><p class="muted">Chưa có máy nào. Bấm "Thêm máy" để nhập máy đầu tiên.</p></div>' : '') + (showForm ? D.formHtml(null, { title: s.devs.length ? 'Thêm máy tiếp theo' : 'Máy chấm công đầu tiên' }) : '<div><button type="button" class="btn" data-act="su-add">' + icon('plus', 18) + (s.devs.length ? 'Thêm máy khác' : 'Thêm máy') + '</button></div>') +
          foot('', nextBtn('Tiếp: Kiểm tra kết nối', s.devs.length > 0));
      }
      if (s.step === 1) {
        const rows = s.devs.map((d) => '<div class="card"><div class="rowsb wrap"><div><div class="fw6 big">' + esc(d.name) + '</div><div class="mono small muted">' + esc(d.host) + '</div></div>' +
          '<button type="button" class="btn" data-act="su-test" data-id="' + d.id + '"' + (s.tests[d.id] && s.tests[d.id].busy ? ' disabled' : '') + '>' + icon('refresh', 18) + 'Kiểm tra lại</button></div>' +
          '<div style="margin-top:12px" data-test="' + d.id + '">' + D.testHtml(s.tests[d.id]) + '</div></div>').join('');
        const failed = s.devs.filter((d) => s.tests[d.id] && !s.tests[d.id].busy && !(s.tests[d.id].result && s.tests[d.id].result.ok));
        return '<div><h1 class="h1 h1-md">Kiểm tra kết nối</h1><p class="sub">App thử đăng nhập từng máy, đọc loại máy và số người đang có.</p></div>' + rows +
          (failed.length ? '<div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span>Máy chưa kết nối được sẽ bị bỏ qua ở bước tiếp theo. Bạn có thể tiếp tục với máy đã kết nối, rồi sửa máy lỗi sau ở "Máy chấm công &amp; cập nhật".</span></div>' : '') +
          foot(backBtn('Sửa máy'), nextBtn('Tiếp: Lấy dữ liệu', okDevs().length > 0));
      }
      if (s.step === 2) {
        const ok = okDevs();
        const i = s.imp;
        let main;
        if (i.phase === 'running') {
          const pct = i.total ? Math.round(i.done * 100 / i.total) : 0;
          main = '<div class="card" role="status"><div class="rowsb fw6"><span>Đang lấy dữ liệu từ ' + esc(srcName()) + '…</span><span class="mono">' + i.done + '/' + i.total + '</span></div>' +
            '<div class="bar" style="margin:10px 0"><div class="bar-in" style="width:' + pct + '%"></div></div><div class="small muted">' + (i.name ? 'Đang đọc: ' + esc(i.name) : 'Đang bắt đầu…') + ' · Nếu bị đứt giữa chừng, bấm lấy lại, app sẽ chạy tiếp.</div></div>';
        } else if (i.phase === 'done') {
          const m = i.summary;
          main = '<div class="notice-ok" role="status">' + icon('checkCircle', 22) + '<div><div class="fw6" style="color:var(--ok-ink)">Đã lấy ' + m.people + ' người từ ' + esc(srcName()) + '</div>' +
            '<div class="small" style="color:#1F3A2C">' + m.withFace + ' người có khuôn mặt · ' + m.fingers + ' vân tay</div></div></div>' +
            (m.facesMissing ? '<div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span>' + m.facesMissing + ' người đã có mặt trên ' + esc(srcName()) + ' nhưng app chưa có ảnh. Cần chụp lại 1 lần nếu muốn gửi sang máy khác; người đó vẫn vào được máy đang có mặt.</span></div>' : '') +
            (i.note ? '<div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span>' + esc(i.note) + ' <a href="#/log">Mở Nhật ký</a></span></div>' : '');
        } else {
          main = '<div class="card"><label class="field-lbl" for="su-src">Lấy dữ liệu từ máy nào?</label>' + W.deviceSelect('su-src', ok, s.source) +
            '<div class="hint">Chọn máy đang có đầy đủ người nhất (thường là máy tầng trệt). App chỉ đọc, không đổi gì trên máy.</div>' + errBox(i.err) +
            '<div style="margin-top:14px"><button type="button" class="btn btn-primary btn-48" data-act="su-import">' + (i.err ? 'Lấy lại dữ liệu' : 'Lấy dữ liệu từ máy này') + '</button></div></div>';
        }
        return '<div><h1 class="h1 h1-md">Lấy dữ liệu từ máy</h1><p class="sub">Chép người, khuôn mặt và vân tay đang có trên máy về app. App sẽ giữ làm bản gốc.</p></div>' + main +
          foot(i.phase === 'running' ? '' : backBtn(),
            (i.phase === 'done' ? '' : (i.phase === 'running' ? '' : '<button type="button" class="btn btn-link" data-act="su-next">Bỏ qua, bắt đầu từ danh sách trống</button>')) +
            (i.phase === 'done' ? nextBtn('Tiếp: Sao lưu', true) : ''));
      }
      if (s.step === 3) {
        return '<div><h1 class="h1 h1-md">Đặt mật khẩu sao lưu</h1><p class="sub">App tự sao lưu mỗi ngày và trước mỗi lần cập nhật. Mật khẩu này dùng để khôi phục khi hỏng máy hoặc cài lại Windows.</p></div>' +
          (s.pwDone ? '<div class="notice-ok" role="status">' + icon('checkCircle', 22) + '<div><div class="fw6" style="color:var(--ok-ink)">Đã đặt mật khẩu sao lưu</div><div class="small" style="color:#1F3A2C">Hãy ghi lại ở nơi an toàn.</div></div></div>' :
            '<form class="card pwform" data-pwform novalidate><div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span><strong>' + PW_WARNING + '.</strong> Hãy ghi lại ở nơi an toàn.</span></div>' +
            '<div class="formgrid"><div><label class="field-lbl" for="su-pw">Mật khẩu (từ 8 ký tự)</label><input id="su-pw" class="input in48" type="password" autocomplete="new-password"></div>' +
            '<div><label class="field-lbl" for="su-pw2">Nhập lại mật khẩu</label><input id="su-pw2" class="input in48" type="password" autocomplete="new-password"></div></div>' +
            '<div data-err>' + errBox(s.pwErr) + '</div><div class="formbtns"><button type="submit" class="btn btn-primary" data-act="pw-save">Đặt mật khẩu</button></div></form>') +
          foot(backBtn(), (s.pwDone ? '' : '<button type="button" class="btn btn-link" data-act="su-next">Để sau</button>') + (s.pwDone ? nextBtn('Tiếp: So khớp', true) : ''));
      }
      const two = s.devs.length >= 2;
      return '<div><h1 class="h1 h1-md">' + (two ? 'So khớp 2 máy' : 'Gần xong rồi') + '</h1>' +
        '<p class="sub">' + (two ? 'App so máy còn lại với dữ liệu vừa lấy, rồi bổ sung phần thiếu.' : 'Bạn mới thêm một máy nên chưa cần so khớp. Khi thêm máy thứ hai, vào "Máy chấm công &amp; cập nhật" để so khớp.') + '</p></div>' +
        (two ? '<div id="su-rc"></div>' : '<div class="notice-ok">' + icon('checkCircle', 22) + '<div class="fw6" style="color:var(--ok-ink)">Đã thiết lập xong</div></div>') +
        foot(backBtn(), '<button type="button" class="btn ' + (two ? 'btn-link' : 'btn-primary btn-next') + '" data-act="su-finish">' + (two ? 'Để sau, vào danh sách' : 'Vào danh sách người dùng') + '</button>');
    }

    function draw() {
      if (!alive()) return;
      if (s.rv) { s.rv.destroy(); s.rv = null; }
      const subs = {};
      host.innerHTML = '<div class="wiz">' + W.stepper(s.step, subs, NAMES) + body() + '</div>';
      const err = host.querySelector('[data-form] [data-err]');
      if (err) err.innerHTML = errBox(s.formErr);
      if (s.step === 4 && s.devs.length >= 2) {
        s.rv = ACS.reconcileView(host.querySelector('#su-rc'), ctx, { doneHtml: '<button type="button" class="btn btn-primary btn-next" data-act="su-finish">Vào danh sách người dùng</button>' });
        s.rv.start();
      }
      if (s.step === 1) s.devs.forEach((d) => { if (!s.tests[d.id]) runTest(d.id); });
    }

    async function runTest(id) {
      s.tests[id] = { busy: true }; if (s.step === 1) draw();
      try { s.tests[id] = { result: await ACS.bridge.call('devices.test', { id }) }; }
      catch (e) { s.tests[id] = { error: ACS.ui.errMessage(e) }; }
      if (alive() && s.step === 1) draw();
    }

    async function loadDevs() { s.devs = await ACS.bridge.call('devices.list'); }
    function pickSource() {
      const ok = okDevs();
      if (!ok.some((d) => d.id === s.source)) s.source = (W.defaultDevice(ok) != null ? W.defaultDevice(ok) : null);
    }
    function go(n) { s.step = n; if (n === 2) pickSource(); draw(); window.scrollTo(0, 0); }

    on(host, 'click', '[data-act="su-add"]', () => { s.adding = true; s.formClosed = false; s.formErr = ''; draw(); });
    on(host, 'click', '[data-act="form-cancel"]', () => { s.adding = false; s.formClosed = true; s.formErr = ''; draw(); });
    on(host, 'click', '[data-act="su-back"]', () => go(Math.max(0, s.step - 1)));
    on(host, 'click', '[data-act="su-next"]', () => go(Math.min(4, s.step + 1)));
    on(host, 'click', '[data-act="su-test"]', (el) => runTest(+el.dataset.id));
    on(host, 'click', '[data-act="su-finish"]', async () => {
      try { if (ACS.refreshState) await ACS.refreshState(); } catch (e) { /* the people screen reloads state itself */ }
      if (alive()) ACS.router.go('people');
    });

    host.addEventListener('submit', async (ev) => {
      const form = ev.target.closest('[data-form]');
      if (form) {
        ev.preventDefault();
        if (s.saving) return;
        const { args, problem } = D.readForm(form, null);
        const errEl = form.querySelector('[data-err]');
        if (problem) { s.formErr = problem; errEl.innerHTML = errBox(problem); return; }
        const btn = form.querySelector('[data-act="form-save"]');
        s.saving = true; btn.disabled = true; btn.textContent = 'Đang lưu…'; errEl.innerHTML = '';
        try {
          await ACS.bridge.call('devices.save', args);
          await loadDevs();
          s.saving = false; if (!alive()) return;
          s.adding = false; s.formErr = ''; draw();
        } catch (e) {
          s.saving = false; if (!alive()) return;
          s.formErr = ACS.ui.errMessage(e); btn.disabled = false; btn.textContent = 'Lưu máy'; errEl.innerHTML = errBox(s.formErr);
        }
        return;
      }
      const pf = ev.target.closest('[data-pwform]');
      if (!pf) return;
      ev.preventDefault();
      const pw = pf.querySelector('#su-pw').value, pw2 = pf.querySelector('#su-pw2').value;
      const problem = R.backupPasswordProblem(pw, pw2);
      const errEl = pf.querySelector('[data-err]');
      if (problem) { errEl.innerHTML = errBox(problem); return; }
      const btn = pf.querySelector('[data-act="pw-save"]'); btn.disabled = true; btn.textContent = 'Đang lưu…';
      try {
        await ACS.bridge.call('backup.setPassword', { password: pw });
        if (!alive()) return;
        s.pwDone = true; draw();
      } catch (e) { if (alive()) { btn.disabled = false; btn.textContent = 'Đặt mật khẩu'; errEl.innerHTML = errBox(ACS.ui.errMessage(e)); } }
    });

    on(host, 'change', '#su-src', (el) => { s.source = +el.value; });
    on(host, 'click', '[data-act="su-import"]', async () => {
      const sel = host.querySelector('#su-src');
      if (sel) s.source = +sel.value;
      s.imp = { phase: 'running', done: 0, total: 0, name: '', summary: null, err: '' }; draw();
      try {
        const sum = await ACS.bridge.call('import.start', { deviceId: s.source });
        if (!alive()) return;
        s.imp = Object.assign({}, s.imp, { phase: 'done', summary: sum });
      } catch (e) {
        if (!alive()) return;
        s.imp = { phase: 'idle', done: 0, total: 0, name: '', summary: null, err: ACS.ui.errMessage(e) };
      }
      draw();
    });
    const offImp = ACS.bridge.on('import.progress', (d) => {
      if (!alive() || s.imp.phase !== 'running') return;
      // The host's last progress event may carry "Không đọc được đủ dữ liệu của N người…" (ImportSummary has no field for it).
      if (d.total && d.done >= d.total && /^Không đọc được/.test(d.currentName || '')) { s.imp.note = d.currentName; return; }
      s.imp.done = d.done; s.imp.total = d.total; s.imp.name = d.currentName;
      if (s.step === 2) draw();
    });

    (async () => {
      try { await loadDevs(); } catch (e) { if (alive()) host.innerHTML = '<div class="wiz">' + errBox(ACS.ui.errMessage(e)) + '</div>'; return; }
      if (!alive()) return;
      draw();
    })();
    return () => { offImp(); if (s.rv) s.rv.destroy(); };
  }

  ACS.screens = ACS.screens || {};
  ACS.screens.setup = { render };
})();
