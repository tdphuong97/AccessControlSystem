// "Máy chấm công & cập nhật": update card, device cards (+ form), reconcile entry, backup, small settings.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const F = ACS.fmt;
  const R = ACS.recon;
  const D = ACS.devui;
  const spin = (n) => icon('spinner', n || 18).replace('class="ic"', 'class="ic xoay"');
  const errBox = (m) => (m ? '<div class="alert alert-err" role="alert">' + icon('alertCircle', 18) + '<span>' + esc(m) + '</span></div>' : '');
  const PW_WARNING = 'Mất mật khẩu này thì không khôi phục được bản sao lưu';

  function statusPill(d) {
    if (d.authPaused) return '<span class="pill pill-err"><span class="dot" style="background:var(--err)"></span>Sai mật khẩu, đã dừng</span>';
    if (!d.online) return '<span class="pill pill-wait"><span class="dot" style="background:var(--wait-dot)"></span>Mất kết nối</span>';
    return '<span class="pill pill-ok"><span class="dot" style="background:var(--ok-dot)"></span>Đang kết nối</span>';
  }

  function render(host, params, ctx) {
    const s = { devs: [], editing: null, tests: {}, formErr: '', saving: false, deleting: {},
      upd: 'idle', pct: 0, updErr: '', checked: false, bak: null, pwOpen: false, pwErr: '', bakBusy: false, bakErr: '', folderErr: '', settings: null, setErr: '' };
    host.innerHTML = '<div class="pad stack">' +
      '<section aria-labelledby="h-upd" class="sec"><h2 id="h-upd" class="h2-lg">Cập nhật phần mềm</h2><div id="s-upd"></div></section>' +
      '<section aria-labelledby="h-dev" class="sec"><div class="pagehead"><h2 id="h-dev" class="h2-lg">Máy chấm công</h2>' +
      '<button type="button" class="btn" data-act="add-dev">' + icon('plus', 18) + 'Thêm máy</button></div>' +
      '<div id="s-form"></div><div class="devgrid" id="s-dev"></div></section>' +
      '<section aria-labelledby="h-rec" class="sec"><h2 id="h-rec" class="h2-lg">So khớp 2 máy</h2><div id="s-rec"></div></section>' +
      '<section aria-labelledby="h-bak" class="sec"><h2 id="h-bak" class="h2-lg">Sao lưu</h2><div id="s-bak"></div></section>' +
      '<section aria-labelledby="h-oth" class="sec"><h2 id="h-oth" class="h2-lg">Khác</h2><div id="s-oth"></div></section></div>';
    const $ = (id) => host.querySelector('#' + id);
    const alive = () => ctx.alive();

    // ---------- update ----------
    function drawUpd() {
      const u = ACS.state && ACS.state.update;
      const ver = ACS.state ? ACS.state.version : '';
      let right = '';
      if (s.upd === 'downloading') {
        right = '<div role="status" class="stackm"><div class="rowsb fw6"><span>Đang tải bản mới…</span><span class="mono">' + s.pct + '%</span></div>' +
          '<div class="bar"><div class="bar-in" style="width:' + s.pct + '%"></div></div><div class="small muted">Vẫn dùng phần mềm bình thường trong lúc tải.</div></div>';
      } else if (s.upd === 'installing') {
        right = '<div role="status" class="notice-info">' + spin(22) + '<div><div class="fw6">Đang cài bản ' + esc(u ? u.version : '') + '…</div>' +
          '<div class="small muted">Phần mềm sẽ tự đóng và mở lại trong giây lát. Dữ liệu được sao lưu trước khi cài.</div></div></div>';
      } else if (u) {
        right = '<div class="rowwrap"><span class="pill pill-solid">Bản mới ' + esc(u.version) + '</span><span class="small muted">' + esc(F.plural(Math.round(u.sizeMb), 'MB')) + '</span></div>' +
          '<p class="upd-notes">' + esc(u.notes) + '</p>' +
          '<div class="rowwrap"><button type="button" class="btn btn-big" data-act="install">' + icon('download', 20) + 'Cập nhật ngay</button>' +
          '<span class="small muted">Mất khoảng 30 giây. Phần mềm tự mở lại, dữ liệu giữ nguyên.</span></div>';
      } else {
        right = '<div class="rowwrap">' + (s.checked ? '<span class="st st-ok">' + icon('checkCircle', 18) + 'Bạn đang dùng bản mới nhất.</span>' : '<span class="muted">Chưa biết có bản mới hay chưa.</span>') +
          '<button type="button" class="btn" data-act="check"' + (s.upd === 'checking' ? ' disabled' : '') + '>' + (s.upd === 'checking' ? spin(18) + 'Đang kiểm tra…' : icon('refresh', 18) + 'Kiểm tra bản mới') + '</button></div>';
      }
      $('s-upd').innerHTML = '<div class="updcard"><div class="updcard-l">' + ACS.icons.LOGO.replace('width="20" height="20"', 'width="56" height="56"') +
        '<div><div class="fw7 big">Access Control System</div><div class="text2">Đang dùng bản <strong>' + esc(ver) + '</strong></div></div></div>' +
        '<div class="updcard-r">' + right + errBox(s.updErr) + '</div></div>';
    }

    on(host, 'click', '[data-act="check"]', async () => {
      s.upd = 'checking'; s.updErr = ''; drawUpd();
      try {
        const u = await ACS.bridge.call('update.check');
        if (!alive()) return;
        s.checked = true; ACS.setUpdate(u);
      } catch (e) { if (!alive()) return; s.updErr = ACS.ui.errMessage(e); }
      s.upd = 'idle'; drawUpd();
    });
    on(host, 'click', '[data-act="install"]', async () => {
      s.upd = 'downloading'; s.pct = 0; s.updErr = ''; drawUpd();
      try { await ACS.bridge.call('update.install'); }
      catch (e) { if (!alive()) return; s.upd = 'idle'; s.updErr = ACS.ui.errMessage(e); drawUpd(); }
    });
    const offProg = ACS.bridge.on('update.progress', (d) => {
      if (!alive() || (s.upd !== 'downloading' && s.upd !== 'installing')) return;
      const f = Math.max(0, Math.min(1, Number(d && d.fraction) || 0));
      s.pct = Math.round(f * 100);
      s.upd = f >= 1 ? 'installing' : 'downloading';
      drawUpd();
    });

    // ---------- devices ----------
    function drawDevs() {
      $('s-dev').innerHTML = s.devs.length ? s.devs.map(devCard).join('') :
        '<div class="card empty"><p class="muted">Chưa có máy nào. Bấm "Thêm máy" để bắt đầu.</p></div>';
    }
    function devCard(d) {
      const t = s.tests[d.id];
      const roles = (d.isFingerReader ? '<span class="pill pill-role">Máy lấy vân tay</span>' : '') + (d.isCaptureDefault ? '<span class="pill pill-role">Máy chụp mặt mặc định</span>' : '');
      const ok = !d.authPaused && d.online;
      const since = !d.online && d.lastSeen ? 'Thấy lần cuối ' + F.timeAgo(d.lastSeen) : '';
      return '<div class="card devc" data-dev="' + d.id + '"><div class="devc-h"><span class="devic ' + (d.authPaused ? 'devic-err' : (ok ? 'devic-ok' : 'devic-wait')) + '">' + icon('device', 22) + '</span>' +
        '<div class="devc-t"><div class="fw7 big">' + esc(d.name) + '</div><div class="rowwrap">' + roles + '</div></div>' + statusPill(d) + '</div>' +
        '<dl class="dl"><dt>Địa chỉ</dt><dd class="mono">' + esc(d.host) + '</dd><dt>Tài khoản</dt><dd>' + esc(d.username) + ' · mật khẩu đã lưu an toàn</dd>' +
        '<dt>Loại máy</dt><dd>' + esc(d.model || 'Chưa rõ') + (d.doorNo ? ' · cửa ' + esc(d.doorNo) : '') + '</dd>' +
        (d.pending || d.failed ? '<dt>Việc chờ</dt><dd>' + d.pending + ' đang chờ' + (d.failed ? ' · ' + d.failed + ' lỗi' : '') + '</dd>' : '') + '</dl>' +
        (d.message && !ok ? '<div class="alert ' + (d.authPaused ? 'alert-err' : 'alert-wait') + '">' + icon('alertCircle', 18) + '<span>' + esc(d.message) + (since ? ' ' + esc(since) + '.' : '') + '</span></div>' : '') +
        '<div data-test>' + D.testHtml(t) + '</div>' +
        '<div class="rowwrap"><button type="button" class="btn" data-act="test" data-id="' + d.id + '"' + (t && t.busy ? ' disabled' : '') + '>' + icon('refresh', 18) + 'Kiểm tra kết nối</button>' +
        '<button type="button" class="btn" data-act="edit" data-id="' + d.id + '">' + icon('edit', 18) + (d.authPaused ? 'Sửa mật khẩu' : 'Sửa') + '</button>' +
        '<button type="button" class="btn btn-danger-o" data-act="del" data-id="' + d.id + '"' + (s.deleting[d.id] ? ' disabled' : '') + '>' + icon('trash', 18) + 'Xoá máy</button></div></div>';
    }
    function drawForm() {
      const box = $('s-form');
      if (s.editing == null) { box.innerHTML = ''; return; }
      const dev = s.editing === 'new' ? null : s.devs.find((d) => d.id === s.editing);
      box.innerHTML = D.formHtml(dev);
      box.querySelector('[data-err]').innerHTML = errBox(s.formErr);
      const f = box.querySelector('[data-f="name"]'); if (f) f.focus();
    }
    async function loadDevs() {
      const list = await ACS.bridge.call('devices.list');
      if (!alive()) return;
      s.devs = list; drawDevs();
    }
    on(host, 'click', '[data-act="add-dev"]', () => { s.editing = 'new'; s.formErr = ''; drawForm(); $('s-form').scrollIntoView({ block: 'nearest' }); });
    on(host, 'click', '[data-act="edit"]', (el) => { s.editing = +el.dataset.id; s.formErr = ''; drawForm(); $('s-form').scrollIntoView({ block: 'nearest' }); });
    on(host, 'click', '[data-act="form-cancel"]', () => { s.editing = null; s.formErr = ''; drawForm(); });
    host.addEventListener('submit', async (ev) => {
      const form = ev.target.closest('[data-form]');
      if (!form) return;
      ev.preventDefault();
      if (s.saving) return;
      const id = s.editing === 'new' ? null : s.editing;
      const { args, problem } = D.readForm(form, id);
      const errEl = form.querySelector('[data-err]');
      if (problem) { s.formErr = problem; errEl.innerHTML = errBox(problem); return; }
      const btn = form.querySelector('[data-act="form-save"]');
      s.saving = true; btn.disabled = true; btn.textContent = 'Đang lưu…'; errEl.innerHTML = '';
      try {
        const saved = await ACS.bridge.call('devices.save', args);
        if (!alive()) return;
        s.editing = null; s.saving = false; drawForm();
        await loadDevs();
        if (ACS.refreshState) ACS.refreshState();
        ACS.ui.toast('Đã lưu ' + saved.name + '. Bấm "Kiểm tra kết nối" để thử máy.');
      } catch (e) {
        s.saving = false;
        if (!alive()) return;
        s.formErr = ACS.ui.errMessage(e); btn.disabled = false; btn.textContent = 'Lưu máy'; errEl.innerHTML = errBox(s.formErr);
      }
    });
    on(host, 'click', '[data-act="test"]', async (el) => {
      const id = +el.dataset.id;
      s.tests[id] = { busy: true }; drawDevs();
      try { s.tests[id] = { result: await ACS.bridge.call('devices.test', { id }) }; }
      catch (e) { s.tests[id] = { error: ACS.ui.errMessage(e) }; }
      if (alive()) drawDevs();
    });
    on(host, 'click', '[data-act="del"]', async (el) => {
      const id = +el.dataset.id;
      const d = s.devs.find((x) => x.id === id);
      if (!d) return;
      const yes = await ACS.ui.confirm({ title: 'Xoá ' + d.name + ' khỏi app?',
        body: 'App sẽ ngừng gửi dữ liệu tới máy này. Người, khuôn mặt và vân tay trong app vẫn giữ nguyên, dữ liệu đang có trên máy chấm công không bị xoá.',
        okText: 'Xoá máy', cancelText: 'Giữ lại', danger: true });
      if (!yes || !alive()) return;
      s.deleting[id] = true; drawDevs();
      try {
        await ACS.bridge.call('devices.delete', { id });
        if (!alive()) return;
        delete s.deleting[id]; delete s.tests[id];
        await loadDevs();
        if (ACS.refreshState) ACS.refreshState();
        ACS.ui.toast('Đã xoá ' + d.name + ' khỏi app.');
      } catch (e) { delete s.deleting[id]; if (alive()) { drawDevs(); ACS.ui.toast(ACS.ui.errMessage(e), 'error'); } }
    });
    const offDev = ACS.bridge.on('devices.changed', (list) => { if (alive() && Array.isArray(list)) { s.devs = list; drawDevs(); } });

    // ---------- reconcile entry ----------
    $('s-rec').innerHTML = '<div class="card recentry"><span class="devic devic-ok">' + icon('refresh', 22) + '</span><div class="grow"><div class="fw6">So khớp người, khuôn mặt và vân tay giữa 2 máy</div>' +
      '<div class="small muted">Dùng khi thay máy, máy bị reset, hoặc có người thêm thẳng trên máy. Xem trước bằng lời trước khi làm; app chỉ gỡ khi bạn chọn "Gỡ khỏi máy", hoặc với người đã khoá trong app.</div></div>' +
      '<a class="btn btn-primary" href="#/reconcile">So khớp 2 máy</a></div>';

    // ---------- backup ----------
    function pwForm() {
      return '<form class="card pwform" data-pwform novalidate><h3 class="h2">' + (s.bak && s.bak.hasPassword ? 'Đổi mật khẩu sao lưu' : 'Đặt mật khẩu sao lưu') + '</h3>' +
        '<div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span><strong>' + PW_WARNING + '.</strong> Hãy ghi lại ở nơi an toàn.</span></div>' +
        '<div class="formgrid"><div><label class="field-lbl" for="bk-pw">Mật khẩu (từ 8 ký tự)</label><input id="bk-pw" class="input in48" type="password" autocomplete="new-password"></div>' +
        '<div><label class="field-lbl" for="bk-pw2">Nhập lại mật khẩu</label><input id="bk-pw2" class="input in48" type="password" autocomplete="new-password"></div></div>' +
        '<div data-err>' + errBox(s.pwErr) + '</div><div class="formbtns">' + (s.bak && s.bak.hasPassword ? '<button type="button" class="btn" data-act="pw-cancel">Huỷ</button>' : '') +
        '<button type="submit" class="btn btn-primary" data-act="pw-save">Lưu mật khẩu</button></div></form>';
    }
    function drawBak() {
      const b = s.bak;
      if (!b) { $('s-bak').innerHTML = '<p class="muted">Đang tải…</p>'; return; }
      const last = b.lastAt ? 'Sao lưu gần nhất: ' + F.timeAgo(b.lastAt) + ' (' + R.fmtDateTime(b.lastAt) + ')' : 'Chưa có bản sao lưu nào.';
      const files = (b.files || []).map((f) => '<li class="bakfile"><span class="bakfile-n"><span class="fw6 mono">' + esc(f.name) + '</span>' +
        '<span class="small muted">' + esc(R.fmtDateTime(f.at)) + ' · ' + esc(R.fmtSize(f.size)) + '</span></span>' +
        '<button type="button" class="btn btn-sm" data-act="restore" data-name="' + esc(f.name) + '"' + (s.bakBusy ? ' disabled' : '') + '>Khôi phục</button></li>').join('');
      $('s-bak').innerHTML = '<div class="card baknote"><div class="rowsb wrap"><div><div class="fw6">' + (b.hasPassword ? 'Đã đặt mật khẩu sao lưu' : 'Chưa đặt mật khẩu sao lưu') + '</div>' +
        '<div class="small muted">' + esc(b.hasPassword ? last : 'Cần đặt mật khẩu trước khi sao lưu.') + '</div></div><div class="rowwrap">' +
        (b.hasPassword ? '<button type="button" class="btn" data-act="pw-open">Đổi mật khẩu</button>' : '') +
        '<button type="button" class="btn btn-primary" data-act="bak-now"' + (!b.hasPassword || s.bakBusy ? ' disabled' : '') + '>' + (s.bakBusy ? spin(18) + 'Đang sao lưu…' : 'Sao lưu ngay') + '</button></div></div>' +
        (b.hasPassword ? '<div class="small muted">' + PW_WARNING + '. App cũng tự sao lưu trước mỗi lần cập nhật.</div>' : '') + errBox(s.bakErr) + '</div>' +
        (!b.hasPassword || s.pwOpen ? pwForm() : '') +
        (files ? '<div class="card"><h3 class="h2">Các bản sao lưu</h3><ul class="bakfiles">' + files + '</ul></div>' : '') +
        '<div class="card"><label class="field-lbl" for="bk-folder">Chép thêm một bản ra thư mục khác (USB hoặc ổ mạng)</label>' +
        '<div class="rowwrap nowrapm"><input id="bk-folder" class="input in48 in-mono grow" type="text" autocomplete="off" placeholder="Ví dụ: E:\\SaoLuuACS" value="' + esc(b.extraFolder || '') + '">' +
        '<button type="button" class="btn" data-act="folder-save">Lưu thư mục</button>' + (b.extraFolder ? '<button type="button" class="btn" data-act="folder-clear">Bỏ thư mục</button>' : '') + '</div>' +
        '<div class="hint">Nên chép bản sao lưu ra nơi khác máy tính này, phòng khi hỏng máy hoặc cài lại Windows.</div><div id="bk-folder-err">' + errBox(s.folderErr) + '</div></div>';
    }
    async function loadBak() {
      try { const b = await ACS.bridge.call('backup.status'); if (!alive()) return; s.bak = b; if (!b.hasPassword) s.pwOpen = true; drawBak(); }
      catch (e) { if (alive()) $('s-bak').innerHTML = errBox(ACS.ui.errMessage(e)); }
    }
    on(host, 'click', '[data-act="pw-open"]', () => { s.pwOpen = true; s.pwErr = ''; drawBak(); });
    on(host, 'click', '[data-act="pw-cancel"]', () => { s.pwOpen = false; s.pwErr = ''; drawBak(); });
    host.addEventListener('submit', async (ev) => {
      const form = ev.target.closest('[data-pwform]');
      if (!form) return;
      ev.preventDefault();
      const pw = form.querySelector('#bk-pw').value, pw2 = form.querySelector('#bk-pw2').value;
      const problem = R.backupPasswordProblem(pw, pw2);
      const errEl = form.querySelector('[data-err]');
      if (problem) { errEl.innerHTML = errBox(problem); return; }
      const btn = form.querySelector('[data-act="pw-save"]'); btn.disabled = true; btn.textContent = 'Đang lưu…';
      try {
        await ACS.bridge.call('backup.setPassword', { password: pw });
        if (!alive()) return;
        s.pwOpen = false; s.pwErr = ''; await loadBak(); ACS.ui.toast('Đã đặt mật khẩu sao lưu. Hãy ghi lại ở nơi an toàn.');
      } catch (e) { if (alive()) { btn.disabled = false; btn.textContent = 'Lưu mật khẩu'; errEl.innerHTML = errBox(ACS.ui.errMessage(e)); } }
    });
    on(host, 'click', '[data-act="bak-now"]', async () => {
      s.bakBusy = true; s.bakErr = ''; drawBak();
      try { await ACS.bridge.call('backup.now'); if (!alive()) return; s.bakBusy = false; await loadBak(); ACS.ui.toast('Đã sao lưu xong.'); }
      catch (e) { s.bakBusy = false; if (alive()) { s.bakErr = ACS.ui.errMessage(e); drawBak(); } }
    });
    async function saveFolder(folder) {
      s.folderErr = '';
      try { await ACS.bridge.call('backup.setExtraFolder', { folder }); if (!alive()) return; await loadBak(); ACS.ui.toast(folder ? 'Đã lưu thư mục sao lưu thêm.' : 'Đã bỏ thư mục sao lưu thêm.'); }
      catch (e) { if (alive()) { s.folderErr = ACS.ui.errMessage(e); drawBak(); } }
    }
    on(host, 'click', '[data-act="folder-save"]', (el) => {
      const v = host.querySelector('#bk-folder').value.trim();
      if (!v) { s.folderErr = 'Hãy nhập đường dẫn thư mục, hoặc bấm "Bỏ thư mục" nếu không cần.'; drawBak(); return; }
      el.disabled = true; saveFolder(v);
    });
    on(host, 'click', '[data-act="folder-clear"]', (el) => { el.disabled = true; saveFolder(null); });
    on(host, 'click', '[data-act="restore"]', (el) => {
      const name = el.dataset.name;
      const f = ((s.bak && s.bak.files) || []).find((x) => x.name === name);
      ACS.ui.dialog('<div class="dlg-body"><h2 class="dlg-title">Khôi phục từ bản sao lưu?</h2>' +
        '<p class="dlg-text">Toàn bộ dữ liệu hiện tại (người, khuôn mặt, vân tay, thiết lập máy) sẽ được thay bằng bản sao lưu <strong class="mono">' + esc(name) + '</strong>' +
        (f ? ' lúc ' + esc(R.fmtDateTime(f.at)) : '') + '. Những thay đổi sau thời điểm đó sẽ mất. App sẽ tự khởi động lại.</p>' +
        '<label class="field-lbl" for="rs-pw" style="margin-top:12px">Mật khẩu sao lưu</label><input id="rs-pw" class="input in48" type="password" autocomplete="off">' +
        '<div class="hint">Mật khẩu bạn đã đặt lúc tạo bản sao lưu này.</div><div data-err></div>' +
        '<div class="dlg-actions"><button type="button" class="btn" data-no>Thôi</button><button type="button" class="btn btn-danger" data-yes>Khôi phục</button></div></div>', {
        onOpen(d) {
          let running = false;
          d.addEventListener('cancel', (ev) => { if (running) ev.preventDefault(); });
          d.addEventListener('click', (ev) => { if (running && ev.target === d) ev.stopImmediatePropagation(); }, true);
          const err = d.querySelector('[data-err]'), yes = d.querySelector('[data-yes]'), no = d.querySelector('[data-no]'), pw = d.querySelector('#rs-pw');
          pw.focus();
          no.addEventListener('click', () => d.finish(false));
          const go = async () => {
            if (!pw.value) { err.innerHTML = errBox('Hãy nhập mật khẩu sao lưu.'); return; }
            running = true; yes.disabled = no.disabled = pw.disabled = true; yes.textContent = 'Đang khôi phục…'; err.innerHTML = '';
            try {
              await ACS.bridge.call('backup.restore', { name, password: pw.value });
              err.innerHTML = '<div class="notice-ok" role="status">' + icon('checkCircle', 18) + '<div>Đã khôi phục. App đang tự khởi động lại…</div></div>';
            } catch (e) { running = false; yes.disabled = no.disabled = pw.disabled = false; yes.textContent = 'Khôi phục'; err.innerHTML = errBox(ACS.ui.errMessage(e)); }
          };
          yes.addEventListener('click', go);
          pw.addEventListener('keydown', (ev) => { if (ev.key === 'Enter') go(); });
        } });
    });

    // ---------- other settings ----------
    function drawOth() {
      const v = s.settings;
      $('s-oth').innerHTML = '<div class="card"><label class="check" style="margin-top:0"><input type="checkbox" data-set="autoCheckUpdates"' + (v && v.autoCheckUpdates ? ' checked' : '') + (v ? '' : ' disabled') + '><span>Tự kiểm tra bản mới mỗi ngày</span></label>' +
        '<label class="check"><input type="checkbox" data-set="startWithWindows"' + (v && v.startWithWindows ? ' checked' : '') + (v ? '' : ' disabled') + '><span>Tự chạy cùng Windows (để việc chờ gửi lên máy không bị bỏ lỡ)</span></label>' + errBox(s.setErr) +
        '<div class="rowwrap" style="margin-top:8px"><a class="btn" href="#/log">Xem nhật ký thao tác</a></div></div>';
    }
    on(host, 'change', '[data-set]', async (el) => {
      el.disabled = true; s.setErr = '';
      try { const v = await ACS.bridge.call('settings.set', { [el.dataset.set]: el.checked }); if (!alive()) return; s.settings = v; }
      catch (e) { if (!alive()) return; s.setErr = ACS.ui.errMessage(e); }
      drawOth();
    });

    drawUpd(); drawDevs(); drawBak(); drawOth();
    (async () => {
      try { await Promise.all([loadDevs(), loadBak(), ACS.bridge.call('settings.get').then((v) => { if (alive()) { s.settings = v; drawOth(); } })]); }
      catch (e) { if (alive()) $('s-dev').innerHTML = errBox(ACS.ui.errMessage(e)); }
    })();
    const offState = ACS.onState(() => { if (alive() && s.upd === 'idle') drawUpd(); });
    return () => { offProg(); offDev(); offState(); };
  }

  ACS.screens = ACS.screens || {};
  ACS.screens.settings = { render, PW_WARNING };
})();
