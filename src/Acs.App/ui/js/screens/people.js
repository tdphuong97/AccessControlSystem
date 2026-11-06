// People list (main screen): update bar, device cards, search, filter chips, table.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const F = ACS.fmt;
  const LIMIT = 20;

  const CHIPS = [
    ['all', 'Tất cả'], ['noFace', 'Chưa có khuôn mặt'], ['noFinger', 'Chưa có vân tay'], ['notSynced', null], ['locked', 'Đã khoá'],
  ];

  function okText(icn, text) { return '<span class="st st-ok">' + icon(icn, 18) + text + '</span>'; }
  function waitText(text) { return '<span class="st st-wait">' + icon('alertCircle', 18) + text + '</span>'; }

  function syncCell(r, n) {
    if (r.locked) return '<span class="pill pill-grey">Đã khoá ra vào</span>';
    if (r.sync === 'ok') return '<span class="st st-ok">' + icon('check', 18) + 'Đủ ' + n + ' máy</span>';
    if (r.sync === 'error') return '<span class="pill pill-err">' + icon('alertCircle', 16) + 'Lỗi gửi, mở để xem</span>';
    const names = (r.waitingDevices || []).join(', ');
    return '<span class="pill pill-wait">' + icon('clock', 16) + 'Chờ gửi ' + esc(names || 'máy') + '</span>';
  }

  function rowHtml(r, n) {
    return '<div class="trow' + (r.locked ? ' is-locked' : '') + '">' +
      '<div class="who"><span class="avatar">' + esc(r.initials || F.initials(r.fullName)) + '</span>' +
      '<div class="who-t"><div class="fw6">' + esc(r.fullName) + '</div><div class="mono small muted">Mã NV ' + esc(r.employeeNo) + '</div></div>' +
      (r.isNew ? '<span class="pill pill-new">Mới</span>' : '') + '</div>' +
      '<div class="c" data-l="Khuôn mặt">' + (r.hasFace ? okText('checkCircle', 'Có') : waitText('Chưa có')) + '</div>' +
      '<div class="c" data-l="Vân tay">' + (r.fingerCount > 0 ? okText('checkCircle', r.fingerCount + ' ngón') : waitText('Chưa có')) + '</div>' +
      '<div class="c" data-l="Có trên ' + n + ' máy">' + syncCell(r, n) + '</div>' +
      '<div class="c open"><a class="btn btn-sm" href="#/person/' + encodeURIComponent(r.employeeNo) + '">Mở' + icon('chevronRight', 16) + '</a></div></div>';
  }

  function deviceCard(d, peopleCount) {
    const role = d.isCaptureDefault ? ' · dùng để chụp mặt và quét vân tay' : '';
    const people = peopleCount + ' người' + role;
    if (d.authPaused) {
      return '<div class="devcard devcard-err"><span class="devic devic-err">' + icon('device', 22) + '</span><div class="devbody">' +
        '<div class="devhead"><span class="devname">' + esc(d.name) + '</span><span class="pill pill-err"><span class="dot" style="background:var(--err)"></span>Sai mật khẩu, đã dừng</span></div>' +
        '<div class="devsub">' + esc(d.message || 'Vào "Máy chấm công & cập nhật" để nhập lại mật khẩu.') + '</div></div>' +
        '<a class="btn" href="#/settings">Sửa mật khẩu</a></div>';
    }
    if (!d.online) {
      const since = d.lastSeen ? ' ' + F.durationText((Date.now() - Date.parse(d.lastSeen)) / 1000) : '';
      const waiting = d.pending > 0 ? 'Có ' + d.pending + ' việc đang chờ. App sẽ tự gửi khi máy có mạng lại.' : 'Chưa có việc nào đang chờ. App sẽ tự kết nối lại khi máy có mạng.';
      return '<div class="devcard devcard-wait"><span class="devic devic-wait">' + icon('device', 22) + '</span><div class="devbody">' +
        '<div class="devhead"><span class="devname">' + esc(d.name) + '</span><span class="pill pill-wait"><span class="dot" style="background:var(--wait-dot)"></span>Mất kết nối' + esc(since) + '</span></div>' +
        '<div class="devsub devsub-wait">' + esc(waiting) + '</div></div>' +
        '<button type="button" class="btn btn-wait" data-act="retry" data-dev="' + d.id + '">' + icon('refresh', 18) + 'Thử lại</button></div>';
    }
    return '<div class="devcard"><span class="devic devic-ok">' + icon('device', 22) + '</span><div class="devbody">' +
      '<div class="devhead"><span class="devname">' + esc(d.name) + '</span><span class="pill pill-ok"><span class="dot" style="background:var(--ok-dot)"></span>Đang kết nối</span></div>' +
      '<div class="devsub">' + esc(people) + '</div></div></div>';
  }

  function render(host) {
    let filter = 'all', query = '', showAll = false, rows = [], counts = null, seq = 0, timer = 0;
    host.innerHTML = '<div class="pad stack">' +
      '<div id="p-upd"></div><div id="p-stale"></div>' +
      '<div class="pagehead"><div><h1 class="h1">Người dùng</h1><p class="sub" id="p-count"></p></div>' +
      '<a class="btn btn-big" href="#/new/info">' + icon('addPerson', 20) + 'Thêm người mới</a></div>' +
      '<div class="devcards" id="p-dev"></div>' +
      '<div class="toolbar"><div class="search"><label for="tim" class="lbl">Tìm người</label><div class="search-in">' +
      '<span class="search-ic">' + icon('search', 18) + '</span>' +
      '<input id="tim" class="input" type="text" autocomplete="off" placeholder="Gõ tên hoặc mã nhân viên…"></div></div>' +
      '<div class="chips" role="group" aria-label="Lọc danh sách" id="p-chips"></div></div>' +
      '<div class="tablebox"><div class="tscroll"><div class="table" id="p-table"></div></div>' +
      '<div class="tfoot"><span class="sub" id="p-note"></span><button type="button" class="btn btn-sm" id="p-all" hidden></button></div></div></div>';
    const $ = (id) => host.querySelector('#' + id);

    const nMachines = () => (ACS.state ? ACS.state.devices.length : 2);

    function drawTop() {
      const st = ACS.state;
      $('p-upd').innerHTML = ACS.updateBar();
      const stale = st && st.staleLocked > 0
        ? '<div class="alert alert-wait">' + icon('clock', 18) + '<span>' + st.staleLocked + ' người đã khoá quá 6 tháng — nên xoá hẳn. ' +
          '<a href="#/people" data-act="show-locked">Xem danh sách đã khoá</a></span></div>' : '';
      $('p-stale').innerHTML = stale;
      const active = counts ? counts.all - counts.locked : 0;
      $('p-count').textContent = counts ? active + ' người đang có quyền ra vào' : '';
      const people = counts ? counts.all - counts.locked : 0;
      $('p-dev').innerHTML = st ? st.devices.map((d) => deviceCard(d, people)).join('') : '';
    }

    function drawChips() {
      if (!counts) return;
      $('p-chips').innerHTML = CHIPS.filter(([id]) => id !== 'locked' || counts.locked > 0 || filter === 'locked').map(([id, label]) => {
        const text = label || 'Chưa đủ ' + nMachines() + ' máy';
        const on_ = id === filter;
        return '<button type="button" class="chip' + (on_ ? ' on' : '') + '" data-chip="' + id + '" aria-pressed="' + on_ + '">' + esc(text) +
          '<span class="chip-n">' + counts[id] + '</span></button>';
      }).join('');
    }

    function drawTable() {
      const n = nMachines();
      const limited = filter === 'all' && !query && !showAll && rows.length > LIMIT;
      const shown = limited ? rows.slice(0, LIMIT) : rows;
      $('p-table').innerHTML = '<div class="thead"><div>Người</div><div>Khuôn mặt</div><div>Vân tay</div><div>Có trên ' + n + ' máy</div><div></div></div>' +
        (shown.length ? shown.map((r) => rowHtml(r, n)).join('')
          : '<div class="empty-row">' + (query || filter !== 'all' ? 'Không có ai khớp. Thử bỏ bớt bộ lọc hoặc gõ tên khác.' : 'Chưa có ai. Bấm "Thêm người mới" để bắt đầu.') + '</div>');
      $('p-note').textContent = !shown.length ? '' : (filter === 'all' && !query
        ? 'Đang hiện ' + shown.length + (limited ? ' người mới thêm gần nhất' : ' người')
        : 'Đang hiện ' + shown.length + ' người' + (filter === 'all' ? ' khớp tìm kiếm' : ' cần bổ sung'));
      const btn = $('p-all');
      btn.hidden = !(filter === 'all' && !query && rows.length > LIMIT);
      btn.textContent = showAll ? 'Chỉ hiện người mới nhất' : 'Xem tất cả ' + rows.length + ' người';
    }

    async function loadCounts() {
      const all = await ACS.bridge.call('people.list', { filter: 'all', query: '' });
      counts = { all: all.length, locked: all.filter((r) => r.locked).length,
        noFace: all.filter((r) => !r.hasFace && !r.locked).length, noFinger: all.filter((r) => !r.fingerCount && !r.locked).length,
        notSynced: all.filter((r) => r.sync !== 'ok' && !r.locked).length };
    }

    async function load() {
      const my = ++seq;
      try {
        const [list] = await Promise.all([ACS.bridge.call('people.list', { filter, query }), loadCounts()]);
        if (my !== seq) return;
        rows = list;
        drawTop(); drawChips(); drawTable();
      } catch (e) {
        $('p-table').innerHTML = '<div class="empty-row">' + esc(ACS.ui.errMessage(e)) + '</div>';
      }
    }

    on(host, 'click', '[data-chip]', (el) => { filter = el.dataset.chip; showAll = false; load(); });
    on(host, 'click', '[data-act="show-locked"]', (el, ev) => { ev.preventDefault(); filter = 'locked'; load(); });
    on(host, 'click', '[data-act="retry"]', async (el) => {
      el.disabled = true;
      try { await ACS.bridge.call('sync.retry', { deviceId: +el.dataset.dev }); ACS.ui.toast('Đã thử gửi lại. Nếu máy vẫn mất kết nối, app sẽ tự thử tiếp.'); }
      catch (e) { ACS.ui.toast(ACS.ui.errMessage(e), 'error'); }
      el.disabled = false;
    });
    $('p-all').addEventListener('click', () => { showAll = !showAll; drawTable(); });
    $('tim').addEventListener('input', (ev) => { clearTimeout(timer); timer = setTimeout(() => { query = ev.target.value.trim(); load(); }, 200); });

    const offState = ACS.onState(() => { if (counts) drawTop(); });
    const offPerson = ACS.bridge.on('person.changed', () => load());
    const offDev = ACS.bridge.on('devices.changed', () => load());
    load();
    return () => { clearTimeout(timer); offState(); offPerson(); offDev(); };
  }

  ACS.screens = ACS.screens || {};
  ACS.screens.people = { render };
})();
