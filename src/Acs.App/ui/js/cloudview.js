// "Dữ liệu chung" section of the settings screen: connect (publish or join), status, on/off switch, sync now,
// check, change password, forget. Returns {destroy()}; opts.onStatus(status) lets the screen know (restore dialog).
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const C = ACS.cloud;
  const errBox = (m) => (m ? '<div class="alert alert-err" role="alert">' + icon('alertCircle', 18) + '<span>' + esc(m) + '</span></div>' : '');
  const spin = (n) => icon('spinner', n || 18).replace('class="ic"', 'class="ic xoay"');
  const LOSE = 'Mất mật khẩu này thì máy mới không tham gia được, không ai lấy lại được';

  function view(root, ctx, opts) {
    opts = opts || {};
    const s = { st: null, busy: '', err: '', check: null };

    function draw() {
      if (!ctx.alive() || !root.isConnected) return;
      const st = s.st;
      if (!st) { root.innerHTML = '<p class="muted">Đang tải…</p>'; return; }
      if (!st.connected) {
        root.innerHTML = '<div class="card"><p style="margin:0 0 8px">Dùng chung một danh sách người, khuôn mặt và vân tay cho nhiều máy tính (ví dụ máy của người làm chính và máy của người làm thay). ' +
          'Dữ liệu được mã hoá trên máy tính trước khi gửi lên Cloudflare; Cloudflare không đọc được.</p>' +
          '<button type="button" class="btn btn-primary" data-act="cl-connect">' + icon('shield', 18) + 'Kết nối dữ liệu chung</button>' + errBox(s.err) + '</div>';
        return;
      }
      const dot = !st.enabled ? 'var(--muted)' : (st.online ? 'var(--ok-dot)' : 'var(--wait-dot)');
      const chk = s.check ? '<div class="small" style="margin-top:8px">' + esc(s.check) + '</div>' : '';
      root.innerHTML = '<div class="card"><div class="rowsb wrap"><div><div class="fw6"><span class="dot" style="background:' + dot + '"></span> ' + esc(C.statusLine(st)) + '</div>' +
        '<div class="small muted">' + esc(C.senderLine(st)) + '</div></div>' +
        '<label class="check" style="margin:0"><input type="checkbox" data-act="cl-switch"' + (st.enabled ? ' checked' : '') + (s.busy ? ' disabled' : '') + '><span>Dùng dữ liệu chung</span></label></div>' +
        '<div class="rowwrap" style="margin-top:12px">' +
        (st.enabled ? '<button type="button" class="btn" data-act="cl-sync"' + (s.busy ? ' disabled' : '') + '>' + (s.busy === 'sync' ? spin() + 'Đang đồng bộ…' : 'Đồng bộ ngay') + '</button>' : '') +
        '<button type="button" class="btn" data-act="cl-check"' + (s.busy ? ' disabled' : '') + '>' + (s.busy === 'check' ? spin() + 'Đang kiểm tra…' : 'Kiểm tra kết nối') + '</button>' +
        '<button type="button" class="btn" data-act="cl-pw"' + (s.busy ? ' disabled' : '') + '>Đổi mật khẩu dữ liệu chung</button></div>' + chk + errBox(s.err || (st.enabled ? st.error && !st.online ? '' : st.error : '')) +
        '<details class="det" style="margin-top:12px"><summary>Nâng cao</summary><p class="small muted">Quên kết nối: xoá mã kết nối và khoá dữ liệu chung khỏi máy này. Máy giữ dữ liệu hiện có và dùng riêng.</p>' +
        '<button type="button" class="btn btn-danger" data-act="cl-forget"' + (s.busy ? ' disabled' : '') + '>Quên kết nối</button></details></div>';
    }

    async function load() {
      try { s.st = await ACS.bridge.call('cloud.status'); if (opts.onStatus) opts.onStatus(s.st); draw(); }
      catch (e) { s.err = ACS.ui.errMessage(e); draw(); }
    }

    async function run(kind, fn) {
      s.busy = kind; s.err = ''; draw();
      try { await fn(); } catch (e) { s.err = ACS.ui.errMessage(e); }
      s.busy = ''; await load();
    }

    function pwFields(creating) {
      return '<label class="field-lbl" for="cl-pw1" style="margin-top:12px">Mật khẩu dữ liệu chung' + (creating ? ' (từ 8 ký tự)' : '') + '</label>' +
        '<input id="cl-pw1" class="input in48" type="password" autocomplete="' + (creating ? 'new-password' : 'off') + '">' +
        (creating ? '<label class="field-lbl" for="cl-pw2" style="margin-top:8px">Nhập lại mật khẩu</label><input id="cl-pw2" class="input in48" type="password" autocomplete="new-password">' : '');
    }

    // Connect: code first, then the password form that fits (create on an empty space, enter it to join).
    function openConnect() {
      ACS.ui.dialog('<div class="dlg-body"><h2 class="dlg-title">Kết nối dữ liệu chung</h2>' +
        '<label class="field-lbl" for="cl-code">Mã kết nối</label><input id="cl-code" class="input in48 in-mono" type="text" autocomplete="off" placeholder="acs1:…">' +
        '<div class="hint">Người quản trị gửi mã này. Chép nguyên dòng.</div><div data-step></div><div data-err></div>' +
        '<div class="dlg-actions"><button type="button" class="btn" data-no>Thôi</button><button type="button" class="btn btn-primary" data-yes>Tiếp tục</button></div></div>', {
        onOpen(d) {
          const code = d.querySelector('#cl-code'), step = d.querySelector('[data-step]'), err = d.querySelector('[data-err]');
          const yes = d.querySelector('[data-yes]'), no = d.querySelector('[data-no]');
          let mode = null;   // 'create' | 'join'
          code.focus();
          no.addEventListener('click', () => d.finish(false));
          yes.addEventListener('click', async () => {
            err.innerHTML = '';
            if (!mode) {
              if (!C.codeLooksValid(code.value)) { err.innerHTML = errBox('Mã kết nối không đúng. Hãy chép lại nguyên dòng mã.'); return; }
              yes.disabled = true;
              try {
                const p = await ACS.bridge.call('cloud.probe', { code: code.value.trim() });
                mode = p.exists ? 'join' : 'create';
                code.disabled = true;
                step.innerHTML = (mode === 'create'
                  ? '<div class="alert alert-wait" role="note" style="margin-top:12px">' + icon('alertCircle', 18) + '<span>Chưa có dữ liệu chung. Máy này sẽ đưa toàn bộ dữ liệu (' + p.people + ' người) lên làm dữ liệu chung. <strong>' + LOSE + '.</strong></span></div>'
                  : '<div class="alert alert-wait" role="note" style="margin-top:12px">' + icon('alertCircle', 18) + '<span>Danh sách trên máy này (' + p.people + ' người) sẽ được thay bằng dữ liệu chung. Phần mềm sao lưu máy này trước.</span></div>') +
                  pwFields(mode === 'create');
                yes.textContent = 'Kết nối';
                d.querySelector('#cl-pw1').focus();
              } catch (e) { err.innerHTML = errBox(ACS.ui.errMessage(e)); }
              yes.disabled = false;
              return;
            }
            const pw = d.querySelector('#cl-pw1').value, pw2 = mode === 'create' ? d.querySelector('#cl-pw2').value : undefined;
            const problem = C.passwordProblem(pw, pw2, mode === 'create');
            if (problem) { err.innerHTML = errBox(problem); return; }
            yes.disabled = no.disabled = true; yes.innerHTML = spin() + (mode === 'create' ? 'Đang đưa dữ liệu lên…' : 'Đang lấy dữ liệu chung…');
            try {
              const r = await ACS.bridge.call('cloud.connect', { code: code.value.trim(), password: pw });
              d.finish(true);
              ACS.ui.toast(r.mode === 'published' ? 'Đã bật dữ liệu chung: đưa ' + r.people + ' người lên.' : 'Đã lấy ' + r.people + ' người từ dữ liệu chung.');
              load();
            } catch (e) { yes.disabled = no.disabled = false; yes.textContent = 'Kết nối'; err.innerHTML = errBox(ACS.ui.errMessage(e)); }
          });
        } });
    }

    function openPassword() {
      ACS.ui.dialog('<div class="dlg-body"><h2 class="dlg-title">Đổi mật khẩu dữ liệu chung</h2>' +
        '<div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span><strong>' + LOSE + '.</strong> Máy đã kết nối không cần nhập lại.</span></div>' +
        pwFields(true) + '<div data-err></div><div class="dlg-actions"><button type="button" class="btn" data-no>Thôi</button><button type="button" class="btn btn-primary" data-yes>Lưu mật khẩu</button></div></div>', {
        onOpen(d) {
          const err = d.querySelector('[data-err]'), yes = d.querySelector('[data-yes]');
          d.querySelector('[data-no]').addEventListener('click', () => d.finish(false));
          yes.addEventListener('click', async () => {
            const pw = d.querySelector('#cl-pw1').value, problem = C.passwordProblem(pw, d.querySelector('#cl-pw2').value, true);
            if (problem) { err.innerHTML = errBox(problem); return; }
            yes.disabled = true;
            try { await ACS.bridge.call('cloud.changePassword', { password: pw }); d.finish(true); ACS.ui.toast('Đã đổi mật khẩu dữ liệu chung. Hãy ghi lại ở nơi an toàn.'); }
            catch (e) { yes.disabled = false; err.innerHTML = errBox(ACS.ui.errMessage(e)); }
          });
        } });
    }

    on(root, 'click', '[data-act="cl-connect"]', openConnect);
    on(root, 'click', '[data-act="cl-pw"]', openPassword);
    on(root, 'click', '[data-act="cl-sync"]', () => run('sync', () => ACS.bridge.call('cloud.syncNow')));
    on(root, 'click', '[data-act="cl-check"]', () => run('check', async () => {
      const r = await ACS.bridge.call('cloud.check');
      s.check = !r.health ? 'Không tới được máy chủ dữ liệu chung. Kiểm tra Internet.'
        : r.auth !== 'ok' ? r.auth
        : 'Kết nối tốt.' + (r.lastSyncAt ? ' Đồng bộ gần nhất lúc ' + r.lastSyncAt.slice(11, 16) + '.' : '') + (r.pending ? ' Còn ' + r.pending + ' thay đổi chưa gửi.' : '');
    }));
    on(root, 'click', '[data-act="cl-forget"]', async () => {
      const ok = await ACS.ui.confirm({ title: 'Quên kết nối dữ liệu chung?', body: 'Máy này ngừng đồng bộ và xoá mã kết nối. Muốn dùng lại phải nhập mã và mật khẩu dữ liệu chung.', okText: 'Quên kết nối', danger: true });
      if (ok) run('forget', () => ACS.bridge.call('cloud.forget'));
    });
    root.addEventListener('change', async (ev) => {
      const box = ev.target.closest('[data-act="cl-switch"]');
      if (!box) return;
      if (box.checked) {
        const ok = await ACS.ui.confirm({ title: 'Bật lại dữ liệu chung?', body: 'Danh sách trên máy này sẽ được thay bằng dữ liệu chung. Phần mềm sao lưu máy này trước. Những gì sửa trên máy này lúc đang tắt sẽ không được đưa lên.', okText: 'Bật lại' });
        if (!ok) { box.checked = false; return; }
      }
      run('switch', () => ACS.bridge.call('cloud.setEnabled', { on: box.checked }));
    });

    const off = ACS.bridge.on('cloud.changed', (st) => { if (ctx.alive() && st) { s.st = st; if (opts.onStatus) opts.onStatus(st); if (!s.busy) draw(); } });
    load();
    return { destroy() { off(); } };
  }

  ACS.cloudView = view;
})();
