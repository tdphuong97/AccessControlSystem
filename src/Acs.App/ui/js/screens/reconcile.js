// "So khớp 2 máy": read -> plan in plain words per device (strangers default "Bỏ qua") -> "Bổ sung" -> progress.
// Also exposes ACS.reconcileView for the first-run flow (same markup, same rules).
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const R = ACS.recon;
  const spin = (n) => icon('spinner', n || 18).replace('class="ic"', 'class="ic xoay"');
  const errBox = (m) => (m ? '<div class="alert alert-err" role="alert">' + icon('alertCircle', 18) + '<span>' + esc(m) + '</span></div>' : '');

  function strangerRow(st, choices) {
    const key = R.strangerKey(st);
    const cur = choices[key] || 'skip';
    const what = [st.faceCount ? st.faceCount + ' khuôn mặt' : 'chưa có khuôn mặt', st.fingerCount ? st.fingerCount + ' vân tay' : 'chưa có vân tay'].join(' · ');
    return '<li class="strow"><div class="strow-w"><div class="fw6">' + esc(st.name || '(không tên)') + ' <span class="mono small muted">Mã NV ' + esc(st.employeeNo) + '</span></div>' +
      '<div class="small muted">Có trên máy nhưng không có trong app · ' + esc(what) + '</div></div>' +
      '<div class="strow-c"><label class="sr-only" for="sc-' + esc(key) + '">Chọn cách xử lý ' + esc(st.name) + '</label>' +
      '<select id="sc-' + esc(key) + '" class="input in48' + (cur === 'remove' ? ' sel-remove' : '') + '" data-stranger="' + esc(key) + '">' +
      R.CHOICES.map((c) => '<option value="' + c + '"' + (c === cur ? ' selected' : '') + '>' + R.CHOICE_LABEL[c] + '</option>').join('') + '</select></div></li>';
  }

  function deviceBlock(d, choices) {
    const items = d.items || [], strangers = d.strangers || [];
    const clean = !items.length && !strangers.length;
    return '<section class="card rdev"><div class="rdev-h"><span class="devic ' + (clean ? 'devic-ok' : 'devic-wait') + '">' + icon('device', 22) + '</span>' +
      '<h2 class="h2 grow">' + esc(d.deviceName) + '</h2><span class="pill ' + (clean ? 'pill-ok' : 'pill-wait') + '">' + esc(R.summarizeDevice(d)) + '</span></div>' +
      (items.length ? '<h3 class="rsub">App sẽ bổ sung</h3><ul class="rlist">' + items.map((i) => '<li class="rli">' + icon('check', 16) + '<span>' + esc(R.describeItem(i)) + '</span></li>').join('') + '</ul>' : '') +
      (strangers.length ? '<h3 class="rsub">Người lạ trên máy</h3><p class="small muted" style="margin:0 0 8px">Mặc định là "Bỏ qua": không đổi gì. Chỉ khi bạn chọn "Gỡ khỏi máy" thì người đó mới bị xoá khỏi máy này.</p>' +
        '<ul class="rlist rlist-st">' + strangers.map((s) => strangerRow(s, choices)).join('') + '</ul>' : '') +
      (clean ? '<p class="muted" style="margin:0">Máy này đã khớp với app, không cần làm gì.</p>' : '') + '</section>';
  }

  // Reusable view: returns {start(), destroy()}. opts.onDone(), opts.intro (html), opts.doneHtml(html).
  function view(root, ctx, opts) {
    opts = opts || {};
    const s = { phase: 'reading', plan: null, choices: {}, msg: '', err: '' };
    let offProg = null;

    function draw() {
      if (!ctx.alive()) return;
      if (s.phase === 'reading') {
        root.innerHTML = '<div class="card rstate" role="status">' + spin(26) + '<div><div class="fw6">Đang đọc máy…</div><div class="small muted">' + esc(s.msg || 'App đang đọc danh sách người trên từng máy để so với bản gốc. Việc này mất vài chục giây.') + '</div></div></div>';
      } else if (s.phase === 'error') {
        root.innerHTML = errBox(s.err) + '<div class="rowwrap" style="margin-top:12px"><button type="button" class="btn btn-primary" data-act="rc-retry">' + icon('refresh', 18) + 'Đọc lại</button></div>';
      } else if (s.phase === 'plan') {
        const tot = R.planTotals(s.plan);
        const nothing = R.nothingToDo(s.plan, s.choices);
        const allClean = !tot.items && !tot.strangers;
        root.innerHTML = '<div class="rplan">' + (s.plan.devices || []).map((d) => deviceBlock(d, s.choices)).join('') + '</div>' + errBox(s.err) +
          '<div class="wfoot"><span class="small muted grow">' + (allClean ? 'Các máy đã khớp với app.' : 'Chưa có gì thay đổi cho tới khi bạn bấm "Bổ sung".') + '</span>' +
          '<button type="button" class="btn" data-act="rc-retry">' + icon('refresh', 18) + 'Đọc lại</button>' +
          '<button type="button" class="btn btn-primary btn-next" data-act="rc-apply"' + (nothing ? ' disabled' : '') + '>Bổ sung</button></div>';
      } else if (s.phase === 'applying') {
        root.innerHTML = '<div class="card rstate" role="status">' + spin(26) + '<div><div class="fw6">Đang bổ sung…</div><div class="small muted">' + esc(s.msg || 'App đang tạo việc gửi cho từng máy.') + '</div></div></div>';
      } else {
        root.innerHTML = '<div class="notice-ok" role="status">' + icon('checkCircle', 22) + '<div><div class="fw6" style="color:var(--ok-ink)">Đã bổ sung xong</div>' +
          '<div class="small" style="color:#1F3A2C">App đã tạo việc gửi phần còn thiếu cho các máy. Máy nào đang mất kết nối sẽ tự nhận khi có mạng lại; theo dõi ở trang Người dùng.</div></div></div>' +
          '<div class="wfoot">' + (opts.doneHtml || '<a class="btn btn-primary btn-next" href="#/people">Về danh sách người dùng</a>') + '</div>';
      }
    }

    async function load() {
      s.phase = 'reading'; s.err = ''; s.msg = ''; draw();
      try {
        const plan = await ACS.bridge.call('reconcile.plan');
        if (!ctx.alive()) return;
        s.plan = plan; s.choices = R.buildStrangers(plan, {}); s.phase = 'plan';
      } catch (e) {
        if (!ctx.alive()) return;
        s.phase = 'error'; s.err = ACS.ui.errMessage(e);
      }
      draw();
    }

    async function apply() {
      const rem = R.removals(s.plan, s.choices);
      if (rem.length) {
        const yes = await ACS.ui.confirm({ title: 'Gỡ người khỏi máy?',
          body: rem.map((r) => r.n + ' người sẽ bị gỡ khỏi ' + r.deviceName).join('; ') + '. Gỡ xong người đó không còn ra vào được bằng máy này, và không có trong app nên không khôi phục lại được. Bạn chắc chắn chứ?',
          okText: 'Gỡ khỏi máy', cancelText: 'Quay lại', danger: true });
        if (!yes || !ctx.alive()) return;
      }
      s.phase = 'applying'; s.msg = ''; s.err = ''; draw();
      try {
        await ACS.bridge.call('reconcile.apply', { strangers: R.buildStrangers(s.plan, s.choices) });
        if (!ctx.alive()) return;
        s.phase = 'done'; draw();
        if (opts.onDone) opts.onDone();
      } catch (e) {
        if (!ctx.alive()) return;
        s.phase = 'plan'; s.err = ACS.ui.errMessage(e); draw();
      }
    }

    on(root, 'click', '[data-act="rc-retry"]', () => load());
    on(root, 'click', '[data-act="rc-apply"]', (el) => { el.disabled = true; apply().then(() => { if (el.isConnected && s.phase === 'plan') el.disabled = R.nothingToDo(s.plan, s.choices); }); });
    root.addEventListener('change', (ev) => {
      const sel = ev.target.closest('[data-stranger]');
      if (!sel) return;
      s.choices[sel.dataset.stranger] = sel.value;
      sel.classList.toggle('sel-remove', sel.value === 'remove');
      const btn = root.querySelector('[data-act="rc-apply"]');
      if (btn) btn.disabled = R.nothingToDo(s.plan, s.choices);
    });
    offProg = ACS.bridge.on('reconcile.progress', (d) => {
      if (!ctx.alive() || (s.phase !== 'reading' && s.phase !== 'applying')) return;
      s.msg = (d && d.message) || '';
      const el = root.querySelector('.rstate .small'); if (el) el.textContent = s.msg;
    });
    return { start: load, destroy() { if (offProg) offProg(); } };
  }

  function render(host, params, ctx) {
    host.innerHTML = '<div class="pad stack"><div class="pagehead"><div><h1 class="h1">So khớp 2 máy</h1>' +
      '<p class="sub">App là bản gốc. So khớp chỉ bổ sung phần còn thiếu cho máy; không bao giờ tự xoá.</p></div>' +
      '<a class="btn" href="#/settings">' + icon('chevronLeft', 18) + 'Về thiết lập</a></div><div id="rc"></div></div>';
    const v = view(host.querySelector('#rc'), ctx, {});
    v.start();
    return () => v.destroy();
  }

  ACS.reconcileView = view;
  ACS.screens = ACS.screens || {};
  ACS.screens.reconcile = { render };
})();
