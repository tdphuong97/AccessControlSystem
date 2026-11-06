// Person page (spec section 6): photo, fingers, one card per device (DeviceSync) and the actions.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const F = ACS.fmt;

  const STATE = {
    ok: { cls: 'ok', text: 'Đã đủ', icon: 'check' },
    pending: { cls: 'wait', text: 'Đang chờ gửi', icon: 'clock' },
    error: { cls: 'err', text: 'Có lỗi', icon: 'alertCircle' },
  };

  function itemHtml(it) {
    const s = STATE[it.state] || STATE.pending;
    return '<li class="sitem"><span class="st st-' + s.cls + '">' + icon(s.icon, 16) + '</span><div class="sitem-b">' +
      '<div><span class="fw5">' + esc(it.label) + '</span> <span class="small st-' + s.cls + '">' + s.text + '</span></div>' +
      (it.message ? '<div class="small">' + esc(it.message) + '</div>' : '') +
      (it.detail ? '<details class="det"><summary>Chi tiết</summary><pre>' + esc(it.detail) + '</pre></details>' : '') + '</div></li>';
  }

  function deviceCard(d) {
    const s = STATE[d.state] || STATE.pending;
    return '<section class="card dcard dcard-' + s.cls + '"><div class="dcard-h"><span class="devic devic-' + s.cls + '">' + icon('device', 22) + '</span>' +
      '<div class="dcard-t"><div class="fw6">' + esc(d.deviceName) + '</div></div>' +
      '<span class="pill pill-' + s.cls + '">' + icon(s.icon, 14) + s.text + '</span></div>' +
      '<ul class="sitems">' + d.items.map(itemHtml).join('') + '</ul>' +
      (d.state !== 'ok' ? '<div class="dcard-f"><button type="button" class="btn btn-sm" data-act="retry" data-dev="' + d.deviceId + '">' + icon('refresh', 16) + 'Thử lại ngay</button></div>' : '') +
      '</section>';
  }

  function render(host, params) {
    const emp = params.emp;
    let p = null;
    host.innerHTML = '<div class="pad stack" id="pp"><p class="muted">Đang tải…</p></div>';
    const root = () => host.querySelector('#pp');

    function draw() {
      const validTo = p.validTo ? 'đến ' + F.fmtDate(p.validTo) : 'không thời hạn';
      const face = p.faceDataUrl
        ? '<img class="photo" alt="Ảnh khuôn mặt của ' + esc(p.fullName) + '" src="' + esc(p.faceDataUrl) + '">'
        : '<div class="photo photo-none"><span class="avatar avatar-lg">' + esc(F.initials(p.fullName)) + '</span><span class="small">Chưa có ảnh mặt</span></div>';
      const fingers = p.fingers.length
        ? '<ul class="flist">' + p.fingers.map((f) => '<li class="frow"><span class="finic">' + icon('fingerprint', 20) + '</span><div class="fl"><div class="fw5">' + esc(f.label) + '</div>' +
          '<div class="small muted">Chất lượng ' + esc(f.quality == null ? 'chưa rõ' : f.quality) + '</div></div>' +
          '<button type="button" class="btn btn-sm btn-icon" data-act="del-finger" data-fid="' + f.fingerId + '" aria-label="Xoá vân tay ' + esc(f.label) + '">' + icon('trash', 18) + '</button></li>').join('') + '</ul>'
        : '<p class="muted small">Chưa có vân tay nào. Bấm "Thêm vân tay" để quét.</p>';
      root().innerHTML =
        '<a class="back" href="#/people">' + icon('chevronLeft', 18) + 'Người dùng</a>' +
        (p.locked ? '<div class="alert alert-wait">' + icon('lock', 18) + '<span>Người này đã bị khoá ra vào. Bấm "Mở lại" để gửi lại cho 2 máy, không cần chụp lại.</span></div>' : '') +
        '<section class="card prof"><div class="prof-photo">' + face + '</div><div class="prof-body">' +
        '<h1 class="h1">' + esc(p.fullName) + '</h1>' +
        '<div class="meta"><span class="mono">Mã NV ' + esc(p.employeeNo) + '</span>' + (p.groupName ? '<span>' + esc(p.groupName) + '</span>' : '') +
        '<span>Hiệu lực từ ' + esc(F.fmtDate(p.validFrom)) + ', ' + validTo + '</span>' +
        (p.locked ? '<span class="pill pill-grey">Đã khoá ra vào</span>' : '<span class="pill pill-ok">Đang có quyền ra vào</span>') + '</div>' +
        '<div class="actions">' +
        (p.locked ? '' : '<button type="button" class="btn" data-act="face">' + icon('camera', 18) + 'Chụp lại mặt</button>' +
          '<button type="button" class="btn" data-act="finger">' + icon('fingerprint', 18) + 'Thêm vân tay</button>') +
        '<button type="button" class="btn" data-act="edit">' + icon('edit', 18) + 'Sửa</button>' +
        (p.locked ? '<button type="button" class="btn btn-primary" data-act="unlock">' + icon('unlock', 18) + 'Mở lại</button>'
          : '<button type="button" class="btn" data-act="lock">' + icon('lock', 18) + 'Khoá ra vào</button>') +
        '<button type="button" class="btn btn-danger-o" data-act="delete">' + icon('trash', 18) + 'Xoá hẳn</button></div></div></section>' +
        '<div class="cols"><section class="card"><div class="card-h"><h2 class="h2">Vân tay</h2></div>' + fingers + '</section>' +
        '<div class="devcol"><h2 class="h2">Trạng thái trên máy</h2>' + p.devices.map(deviceCard).join('') + '</div></div>';
    }

    async function load() {
      try { p = await ACS.bridge.call('people.get', { employeeNo: emp }); draw(); }
      catch (e) { root().innerHTML = '<a class="back" href="#/people">' + icon('chevronLeft', 18) + 'Người dùng</a><div class="alert alert-err">' + esc(ACS.ui.errMessage(e)) + '</div>'; }
    }
    const guard = async (fn) => { try { await fn(); } catch (e) { ACS.ui.toast(ACS.ui.errMessage(e), 'error'); } };

    function editDialog() {
      const noEnd = !p.validTo;
      const html = '<form class="dlg-body" novalidate><h2 class="dlg-title">Sửa thông tin</h2>' +
        '<label class="lbl" for="e-name">Họ tên</label><input id="e-name" class="input" maxlength="32" value="' + esc(p.fullName) + '">' +
        '<label class="lbl" for="e-group">Nhóm (không bắt buộc)</label><input id="e-group" class="input" value="' + esc(p.groupName || '') + '">' +
        '<label class="check"><input type="checkbox" id="e-open"' + (noEnd ? ' checked' : '') + '> Không thời hạn</label>' +
        '<label class="lbl" for="e-to">Hết hạn ra vào</label><input id="e-to" type="date" class="input" value="' + esc(p.validTo || '') + '"' + (noEnd ? ' disabled' : '') + '>' +
        '<div class="alert alert-err" id="e-err" hidden></div>' +
        '<div class="dlg-actions"><button type="button" class="btn" data-no>Thôi</button><button type="submit" class="btn btn-primary">Lưu</button></div></form>';
      return ACS.ui.dialog(html, {
        onOpen(d) {
          const q = (s) => d.querySelector(s);
          q('[data-no]').addEventListener('click', () => d.finish(null));
          q('#e-open').addEventListener('change', (ev) => { q('#e-to').disabled = ev.target.checked; });
          q('form').addEventListener('submit', async (ev) => {
            ev.preventDefault();
            const fullName = q('#e-name').value.trim();
            if (!fullName) { const b = q('#e-err'); b.hidden = false; b.textContent = 'Hãy nhập họ tên rồi bấm Lưu.'; return; }
            try {
              const r = await ACS.bridge.call('people.update', { employeeNo: emp, fullName, groupName: q('#e-group').value.trim() || null,
                validTo: q('#e-open').checked ? null : (q('#e-to').value || null) });
              d.finish(r);
            } catch (e) { const b = q('#e-err'); b.hidden = false; b.textContent = ACS.ui.errMessage(e); }
          });
          q('#e-name').focus();
        },
      });
    }

    on(host, 'click', '[data-act]', (el) => {
      const act = el.dataset.act;
      if (act === 'face') ACS.router.go('new/face', emp);
      else if (act === 'finger') ACS.router.go('new/finger', emp);
      else if (act === 'edit') guard(async () => { const r = await editDialog(); if (r) { p = r; draw(); ACS.ui.toast('Đã lưu thông tin.'); } });
      else if (act === 'lock') guard(async () => {
        const ok = await ACS.ui.confirm({ title: 'Khoá ra vào cho ' + p.fullName + '?',
          body: 'Người này sẽ không ra vào được nữa. App vẫn giữ mặt và vân tay, bấm "Mở lại" bất cứ lúc nào để dùng tiếp.', okText: 'Khoá ra vào' });
        if (ok) { p = await ACS.bridge.call('people.lock', { employeeNo: emp }); draw(); }
      });
      else if (act === 'unlock') guard(async () => { p = await ACS.bridge.call('people.unlock', { employeeNo: emp }); draw(); ACS.ui.toast('Đã mở lại. App đang gửi cho 2 máy.'); });
      else if (act === 'delete') guard(async () => {
        const ok = await ACS.ui.confirm({ title: 'Xoá hẳn ' + p.fullName + '?',
          body: 'App sẽ gỡ người này khỏi cả 2 máy và xoá luôn mặt, vân tay trong app. Không khôi phục lại được.', okText: 'Xoá hẳn', danger: true });
        if (ok) { await ACS.bridge.call('people.delete', { employeeNo: emp }); ACS.router.go('people'); }
      });
      else if (act === 'del-finger') guard(async () => {
        const fid = +el.dataset.fid;
        const f = p.fingers.find((x) => x.fingerId === fid);
        const ok = await ACS.ui.confirm({ title: 'Xoá vân tay ' + (f ? f.label : '') + '?', body: 'Vân tay này sẽ được gỡ khỏi cả 2 máy.', okText: 'Xoá vân tay', danger: true });
        if (ok) { p = await ACS.bridge.call('finger.delete', { employeeNo: emp, fingerId: fid }); draw(); }
      });
      else if (act === 'retry') guard(async () => {
        await ACS.bridge.call('sync.retry', { deviceId: +el.dataset.dev });
        ACS.ui.toast('Đã thử gửi lại. Nếu máy vẫn mất kết nối, app sẽ tự thử tiếp.');
        p.devices = await ACS.bridge.call('sync.person', { employeeNo: emp }); draw();
      });
    });

    const off = ACS.bridge.on('person.changed', (d) => { if (d && String(d.employeeNo) === String(emp)) load(); });
    load();
    return off;
  }

  ACS.screens = ACS.screens || {};
  ACS.screens.person = { render };
})();
