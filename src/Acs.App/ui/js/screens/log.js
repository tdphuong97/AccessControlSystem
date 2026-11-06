// Nhật ký: list of operations and errors, filtered by device, errors only, and free text.
(function () {
  const ACS = window.ACS;
  const { esc } = ACS.ui;
  const icon = ACS.icons.icon;
  const R = ACS.recon;

  function render(host, params, ctx) {
    const s = { rows: null, deviceId: '', q: '', onlyErrors: false, err: '', seq: 0 };
    const devs = (ACS.state && ACS.state.devices) || [];
    host.innerHTML = '<div class="pad stack"><div class="pagehead"><div><h1 class="h1">Nhật ký</h1>' +
      '<p class="sub">Các thao tác gửi lên máy và lỗi gần đây (tối đa 200 dòng mới nhất).</p></div>' +
      '<a class="btn" href="#/settings">' + icon('chevronLeft', 18) + 'Quay lại</a></div>' +
      '<div class="toolbar"><div class="search"><label for="lg-q" class="lbl">Tìm trong nhật ký</label><div class="search-in"><span class="search-ic">' + icon('search', 18) + '</span>' +
      '<input id="lg-q" class="input" type="text" autocomplete="off" placeholder="Gõ mã nhân viên, tên máy hoặc nội dung…"></div></div>' +
      '<div class="logf"><div><label for="lg-dev" class="lbl">Máy</label><select id="lg-dev" class="input"><option value="">Tất cả máy</option>' +
      devs.map((d) => '<option value="' + d.id + '">' + esc(d.name) + '</option>').join('') + '</select></div>' +
      '<label class="check logf-chk"><input type="checkbox" id="lg-err"><span>Chỉ hiện lỗi</span></label></div></div>' +
      '<div class="tablebox"><div id="lg-list" role="status"><div class="empty-row">Đang tải…</div></div><div class="tfoot"><span class="sub" id="lg-note"></span></div></div></div>';
    const $ = (id) => host.querySelector('#' + id);

    function draw() {
      if (s.err) { $('lg-list').innerHTML = '<div class="empty-row">' + esc(s.err) + '</div>'; $('lg-note').textContent = ''; return; }
      if (!s.rows) return;
      const rows = R.filterLog(s.rows, { onlyErrors: s.onlyErrors, q: s.q });
      $('lg-list').innerHTML = rows.length ? rows.map((r) =>
        '<div class="lrow' + (r.ok ? '' : ' lrow-err') + '"><div class="lrow-t mono small">' + esc(R.fmtDateTime(r.at)) + '</div>' +
        '<div class="lrow-s">' + (r.ok ? '<span class="st st-ok">' + icon('checkCircle', 18) + 'Xong</span>' : '<span class="st st-err">' + icon('alertCircle', 18) + 'Lỗi</span>') + '</div>' +
        '<div class="lrow-m"><div class="fw6">' + esc(r.action) + '</div><div class="small muted">' + esc(r.deviceName || 'Không gắn với máy nào') +
        (r.employeeNo ? ' · Mã NV <span class="mono">' + esc(r.employeeNo) + '</span>' : '') + '</div>' +
        (r.message ? '<div class="small ' + (r.ok ? 'muted' : 'lrow-msg') + '">' + esc(r.message) + '</div>' : '') + '</div></div>').join('')
        : '<div class="empty-row">' + (s.rows.length ? 'Không có dòng nào khớp. Thử bỏ bớt bộ lọc.' : 'Nhật ký còn trống. Khi app gửi dữ liệu lên máy, các thao tác sẽ hiện ở đây.') + '</div>';
      $('lg-note').textContent = rows.length ? 'Đang hiện ' + rows.length + ' dòng' + (rows.length !== s.rows.length ? ' trên ' + s.rows.length : '') : '';
    }

    async function load() {
      const my = ++s.seq;
      try {
        const args = { limit: 200 };
        if (s.deviceId) args.deviceId = +s.deviceId;
        const rows = await ACS.bridge.call('log.list', args);
        if (my !== s.seq || !ctx.alive()) return;
        s.rows = rows; s.err = '';
      } catch (e) {
        if (my !== s.seq || !ctx.alive()) return;
        s.err = ACS.ui.errMessage(e);
      }
      draw();
    }

    let timer = 0;
    $('lg-q').addEventListener('input', (ev) => { clearTimeout(timer); timer = setTimeout(() => { s.q = ev.target.value; draw(); }, 150); });
    $('lg-err').addEventListener('change', (ev) => { s.onlyErrors = ev.target.checked; draw(); });
    $('lg-dev').addEventListener('change', (ev) => { s.deviceId = ev.target.value; load(); });
    load();
    return () => { s.seq++; clearTimeout(timer); };
  }

  ACS.screens = ACS.screens || {};
  ACS.screens.log = { render };
})();
