// "Lấy dữ liệu từ máy" outside the first-run wizard: pick a device the app can read, copy its people, faces and
// fingerprints into the app (import.start), show progress and the result. Used by the settings screen and by So khớp
// when the app has nobody yet. Returns {start(), destroy()}; opts.intro (html), opts.doneHtml (html), opts.onStart(), opts.onDone(summary).
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const R = ACS.recon;
  const W = ACS.wizard;

  function view(root, ctx, opts) {
    opts = opts || {};
    const s = { phase: 'loading', devs: [], source: null, done: 0, total: 0, name: '', note: '', summary: null, err: '' };
    const srcName = () => { const d = s.devs.find((x) => x.id === s.source); return d ? d.name : 'máy'; };

    function draw() {
      if (!ctx.alive() || !root.isConnected) return;
      const ok = R.importSources(s.devs);
      let html;
      if (s.phase === 'loading') {
        html = '<p class="muted">Đang tải…</p>';
      } else if (s.phase === 'running') {
        const pct = s.total ? Math.round(s.done * 100 / s.total) : 0;
        html = '<div class="card" role="status"><div class="rowsb fw6"><span>Đang lấy dữ liệu từ ' + esc(srcName()) + '…</span><span class="mono">' + s.done + '/' + s.total + '</span></div>' +
          '<div class="bar" style="margin:10px 0"><div class="bar-in" style="width:' + pct + '%"></div></div><div class="small muted">' + (s.name ? 'Đang đọc: ' + esc(s.name) : 'Đang bắt đầu…') +
          ' · Nếu bị đứt giữa chừng, bấm lấy lại, app sẽ chạy tiếp.</div></div>';
      } else if (s.phase === 'done') {
        const m = s.summary;
        html = '<div class="notice-ok" role="status">' + icon('checkCircle', 22) + '<div><div class="fw6" style="color:var(--ok-ink)">Đã lấy ' + m.people + ' người từ ' + esc(srcName()) + '</div>' +
          '<div class="small" style="color:#1F3A2C">' + m.withFace + ' người có khuôn mặt · ' + m.fingers + ' vân tay</div></div></div>' +
          (m.facesMissing ? '<div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span>' + m.facesMissing + ' người đã có mặt trên ' + esc(srcName()) + ' nhưng app chưa có ảnh. Cần chụp lại 1 lần nếu muốn gửi sang máy khác; người đó vẫn vào được máy đang có mặt.</span></div>' : '') +
          (s.note ? '<div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span>' + esc(s.note) + ' <a href="#/log">Mở Nhật ký</a></span></div>' : '') +
          (opts.doneHtml ? '<div class="rowwrap" style="margin-top:12px">' + opts.doneHtml + '</div>' : '');
      } else if (!ok.length) {
        html = '<div class="alert alert-wait" role="note">' + icon('alertCircle', 18) + '<span>Chưa có máy nào đang kết nối nên chưa lấy được dữ liệu. Kiểm tra máy ở tab "Máy chấm công &amp; cập nhật" rồi bấm Đọc lại.</span></div>' +
          W.errBox(s.err) + '<div class="rowwrap" style="margin-top:12px"><button type="button" class="btn" data-act="iv-reload">' + icon('refresh', 18) + 'Đọc lại</button></div>';
      } else {
        html = '<div class="card">' + (opts.intro || '') + '<label class="field-lbl" for="iv-src">Lấy dữ liệu từ máy nào?</label>' + W.deviceSelect('iv-src', ok, s.source) +
          '<div class="hint">Chọn máy đang có đầy đủ người nhất (thường là máy tầng trệt). App chỉ đọc, không đổi gì trên máy. Người đã có trong app được giữ nguyên, app chỉ bổ sung phần còn thiếu.</div>' + W.errBox(s.err) +
          '<div style="margin-top:14px"><button type="button" class="btn btn-primary btn-48" data-act="iv-import">' + (s.err ? 'Lấy lại dữ liệu' : 'Lấy dữ liệu từ máy này') + '</button></div></div>';
      }
      root.innerHTML = html;
    }

    async function loadDevs() {
      s.err = '';
      try { s.devs = await ACS.bridge.call('devices.list'); } catch (e) { s.devs = []; s.err = ACS.ui.errMessage(e); }
      if (!ctx.alive()) return;
      const ok = R.importSources(s.devs);
      if (!ok.some((d) => d.id === s.source)) s.source = W.defaultDevice(ok);
      s.phase = 'idle'; draw();
    }

    on(root, 'change', '#iv-src', (el) => { s.source = +el.value; });
    on(root, 'click', '[data-act="iv-reload"]', () => { s.phase = 'loading'; draw(); loadDevs(); });
    on(root, 'click', '[data-act="iv-import"]', async () => {
      const sel = root.querySelector('#iv-src');
      if (sel) s.source = +sel.value;
      Object.assign(s, { phase: 'running', done: 0, total: 0, name: '', note: '', summary: null, err: '' }); draw();
      if (opts.onStart) opts.onStart();
      try {
        const sum = await ACS.bridge.call('import.start', { deviceId: s.source });
        if (ACS.refreshState) ACS.refreshState().catch(() => {});   // people count and the So khớp notice changed
        if (!ctx.alive()) return;
        s.phase = 'done'; s.summary = sum; draw();
        if (opts.onDone) opts.onDone(sum);
      } catch (e) {
        if (!ctx.alive()) return;
        s.phase = 'idle'; s.err = ACS.ui.errMessage(e);
        const keep = s.source;
        await loadDevs();   // the device may have gone offline or been paused meanwhile
        if (R.importSources(s.devs).some((d) => d.id === keep)) s.source = keep;
        s.err = s.err || ACS.ui.errMessage(e); draw();
      }
    });
    const offImp = ACS.bridge.on('import.progress', (d) => {
      if (!ctx.alive() || s.phase !== 'running') return;
      // The host's last progress event may carry "Không đọc được đủ dữ liệu của N người…" (ImportSummary has no field for it).
      if (d.total && d.done >= d.total && /^Không đọc được/.test(d.currentName || '')) { s.note = d.currentName; return; }
      s.done = d.done; s.total = d.total; s.name = d.currentName;
      draw();
    });

    return { start() { draw(); loadDevs(); }, destroy() { offImp(); } };
  }

  ACS.importView = view;
})();
