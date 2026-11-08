// People list (main screen): update bar, device cards, search, filter chips, table.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const F = ACS.fmt;
  const SIZE_KEY = 'acs.people.pageSize';   // per-PC convenience only: lost storage just means the default 50
  const savedSize = () => { try { return localStorage.getItem(SIZE_KEY); } catch (e) { return null; } };
  const saveSize = (n) => { try { localStorage.setItem(SIZE_KEY, String(n)); } catch (e) { /* not kept */ } };

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
      '<div class="devhead"><span class="devname">' + esc(d.name) + '</span><span class="pill pill-ok"><span class="dot" style="background:var(--ok-dot)"></span>' + ACS.recon.deviceStatus(d).pill + '</span></div>' +
      '<div class="devsub">' + esc(people) + '</div></div></div>';
  }

  function render(host, params, ctx) {
    let filter = 'all', query = '', group = '', groups = [], noGroup = 0, rows = [], counts = null, seq = 0, timer = 0, size = F.pageSize(savedSize()), page = 1;
    host.innerHTML = '<div class="pad stack">' +
      '<div id="p-upd"></div><div id="p-imp"></div><div id="p-rec"></div><div id="p-stale"></div>' +
      '<div class="pagehead"><div><h1 class="h1">Người dùng</h1><p class="sub" id="p-count"></p></div>' +
      '<a class="btn btn-big" href="#/new/info">' + icon('addPerson', 20) + 'Thêm người mới</a></div>' +
      '<div class="devcards" id="p-dev"></div>' +
      '<div class="toolbar"><div class="search"><label for="tim" class="lbl">Tìm người</label><div class="search-in">' +
      '<span class="search-ic">' + icon('search', 18) + '</span>' +
      '<input id="tim" class="input" type="text" autocomplete="off" placeholder="Gõ tên hoặc mã nhân viên…"></div></div>' +
      '<div class="grp" id="p-grp"></div>' +
      '<div class="chips" role="group" aria-label="Lọc danh sách" id="p-chips"></div></div>' +
      '<div class="tablebox"><div class="tscroll"><div class="table" id="p-table"></div></div>' +
      '<div class="tfoot"><span class="sub" id="p-note"></span><div class="pager" id="p-pager"></div></div></div></div>';
    const $ = (id) => host.querySelector('#' + id);

    const nMachines = () => (ACS.state ? ACS.state.devices.length : 2);

    function drawTop() {
      const st = ACS.state;
      $('p-upd').innerHTML = ACS.updateBar();
      const stale = st && st.staleLocked > 0
        ? '<div class="alert alert-wait">' + icon('clock', 18) + '<span>' + st.staleLocked + ' người đã khoá quá 6 tháng — nên xoá hẳn. ' +
          '<a href="#/people" data-act="show-locked">Xem danh sách đã khoá</a></span></div>' : '';
      $('p-stale').innerHTML = stale;
      // A new device or a restore: nothing is queued for that device until So khớp runs (app.state.needsReconcile).
      $('p-rec').innerHTML = st && st.needsReconcile && st.devices.length
        ? '<div class="alert alert-wait" role="note">' + icon('refresh', 18) + '<span>Có máy mới, máy vừa đổi, vừa khôi phục hoặc vừa lấy dữ liệu — ' +
          'chạy So khớp 2 máy để gửi đủ cho mọi máy. <a href="#/reconcile">Mở So khớp 2 máy</a></span></div>' : '';
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

    // "Công ty": only once someone has a group. A group that no longer has anyone falls back to everyone.
    function drawGroups() {
      if (group && group !== F.NO_GROUP && !groups.some((g) => g.name === group)) group = '';
      const opt = (v, t) => '<option value="' + esc(v) + '"' + (v === group ? ' selected' : '') + '>' + esc(t) + '</option>';
      $('p-grp').innerHTML = !groups.length ? '' : '<label class="lbl" for="p-group">Công ty</label><select id="p-group" class="input">' +
        opt('', 'Tất cả công ty') + groups.map((g) => opt(g.name, g.name + ' (' + g.count + ')')).join('') +
        (noGroup ? opt(F.NO_GROUP, 'Chưa có nhóm (' + noGroup + ')') : '') + '</select>';
    }

    function drawTable() {
      const n = nMachines();
      const list = F.byGroup(rows, group);
      const pg = F.paginate(list.length, size, page);
      page = pg.page;
      const shown = list.slice(pg.from, pg.to);
      $('p-table').innerHTML = '<div class="thead"><div>Người</div><div>Khuôn mặt</div><div>Vân tay</div><div>Có trên ' + n + ' máy</div><div></div></div>' +
        (shown.length ? shown.map((r) => rowHtml(r, n)).join('')
          : '<div class="empty-row">' + (query || filter !== 'all' || group ? 'Không có ai khớp. Thử bỏ bớt bộ lọc hoặc gõ tên khác.' : 'Chưa có ai. Bấm "Thêm người mới" để bắt đầu.') + '</div>');
      // The list is sorted by name (vi-VN), so the first page is not "the newest people".
      const of = !group ? '' : group === F.NO_GROUP ? ' chưa có nhóm' : ' của ' + group;
      const what = of + (filter === 'all' ? (query ? ' khớp tìm kiếm' : of ? '' : ', theo tên') : ' cần bổ sung');
      $('p-note').textContent = !shown.length ? '' : (pg.pages > 1
        ? 'Đang hiện ' + (pg.from + 1) + '–' + pg.to + ' trong ' + list.length + ' người' + what
        : 'Đang hiện ' + list.length + ' người' + (what === ', theo tên' ? '' : what));
      // The page-size choice only matters once there is more than the smallest page.
      $('p-pager').innerHTML = list.length <= F.PAGE_SIZES[0] ? '' :
        '<label class="pager-size" for="p-size">Mỗi trang<select id="p-size" class="input pager-sel">' +
        F.PAGE_SIZES.map((s) => '<option value="' + s + '"' + (s === size ? ' selected' : '') + '>' + (s ? s : 'Tất cả') + '</option>').join('') + '</select></label>' +
        (pg.pages > 1 ? '<button type="button" class="btn btn-sm" data-act="pg-prev"' + (pg.page <= 1 ? ' disabled' : '') + ' aria-label="Trang trước">' + icon('chevronLeft', 16) + 'Trước</button>' +
          '<span class="pager-n" aria-live="polite">Trang ' + pg.page + '/' + pg.pages + '</span>' +
          '<button type="button" class="btn btn-sm" data-act="pg-next"' + (pg.page >= pg.pages ? ' disabled' : '') + ' aria-label="Trang sau">Sau' + icon('chevronRight', 16) + '</button>' : '');
    }
    function goPage(n) {
      page = n; drawTable();
      const box = host.querySelector('.tablebox');
      if (box && box.getBoundingClientRect().top < 0) box.scrollIntoView({ block: 'start' });
    }

    async function loadCounts() {
      const all = await ACS.bridge.call('people.list', { filter: 'all', query: '' });
      counts = { all: all.length, locked: all.filter((r) => r.locked).length,
        noFace: all.filter((r) => !r.hasFace && !r.locked).length, noFinger: all.filter((r) => !r.fingerCount && !r.locked).length,
        notSynced: all.filter((r) => r.sync !== 'ok' && !r.locked).length };
      groups = F.groupList(all);
      noGroup = all.filter((r) => !r.groupName).length;
    }

    async function load() {
      const my = ++seq;
      try {
        const [list] = await Promise.all([ACS.bridge.call('people.list', { filter, query }), loadCounts()]);
        if (my !== seq || !ctx.alive()) return;
        rows = list;
        drawTop(); drawChips(); drawGroups(); drawTable();
      } catch (e) {
        if (my !== seq || !ctx.alive()) return;
        $('p-table').innerHTML = '<div class="empty-row">' + esc(ACS.ui.errMessage(e)) + '</div>';
      }
    }

    on(host, 'click', '[data-chip]', (el) => { filter = el.dataset.chip; page = 1; load(); });
    on(host, 'click', '[data-act="show-locked"]', (el, ev) => { ev.preventDefault(); filter = 'locked'; page = 1; load(); });
    on(host, 'change', '#p-group', (el) => { group = el.value; page = 1; drawTable(); });
    on(host, 'change', '#p-size', (el) => { size = F.pageSize(el.value); saveSize(size); page = 1; drawTable(); });
    on(host, 'click', '[data-act="pg-prev"]', () => goPage(page - 1));
    on(host, 'click', '[data-act="pg-next"]', () => goPage(page + 1));
    on(host, 'click', '[data-act="retry"]', async (el) => {
      el.disabled = true;
      try { await ACS.bridge.call('sync.retry', { deviceId: +el.dataset.dev }); ACS.ui.toast('Đã thử gửi lại. Nếu máy vẫn mất kết nối, app sẽ tự thử tiếp.'); }
      catch (e) { ACS.ui.toast(ACS.ui.errMessage(e), 'error'); }
      el.disabled = false;
    });
    $('tim').addEventListener('input', (ev) => { clearTimeout(timer); timer = setTimeout(() => { query = ev.target.value.trim(); page = 1; load(); }, 200); });

    // An import started on another screen keeps running in the host: say so here and refresh the list when it ends.
    let imp = null;
    function drawImp() {
      const text = ACS.recon.importNotice(imp);
      $('p-imp').innerHTML = text ? '<div class="notice-info" role="status">' + icon('refresh', 18).replace('class="ic"', 'class="ic xoay"') +
        '<span>' + esc(text) + '. Danh sách tự cập nhật khi xong. <a href="#/settings">Xem chi tiết</a></span></div>' : '';
    }
    ACS.bridge.call('import.status').then((st) => { if (ctx.alive()) { imp = st; drawImp(); } }).catch(() => {});
    const offImpProg = ACS.bridge.on('import.progress', (d) => {
      // Only an import this screen already knows about (import.status): a late event never brings a notice back.
      if (!ctx.alive() || !d || !imp || !imp.running) return;
      imp = Object.assign({}, imp, { done: d.done, total: d.total });
      if (/^Không đọc được/.test(d.currentName || '')) return;
      drawImp();
    });
    const offImpDone = ACS.bridge.on('import.done', () => {
      if (!ctx.alive()) return;
      imp = null; drawImp(); load();
      if (ACS.refreshState) ACS.refreshState().catch(() => {});
    });

    const offState = ACS.onState(() => { if (counts && ctx.alive()) drawTop(); });
    const offPerson = ACS.bridge.on('person.changed', () => load());
    const offDev = ACS.bridge.on('devices.changed', () => load());
    load();
    return () => { seq++; clearTimeout(timer); offState(); offPerson(); offDev(); offImpProg(); offImpDone(); };
  }

  ACS.screens = ACS.screens || {};
  ACS.screens.people = { render };
})();
