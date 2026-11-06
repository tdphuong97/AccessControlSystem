// Wizard step 4 - Hoàn tất. One card per device; a waiting device turns into a green card by itself on person.changed.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const W = ACS.wizard;
  const F = ACS.fmt;

  const ST = {
    ok: { cls: 'ok', icon: 'checkCircle', text: 'Đã nhận' },
    pending: { cls: 'wait', icon: 'clock', text: 'Đang chờ' },
    error: { cls: 'err', icon: 'alertCircle', text: 'Có lỗi' },
  };
  const hhmm = () => { const d = new Date(); return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0'); };

  function render(host, params, ctx) {
    const emp = params.emp;
    if (!emp) { ACS.router.go('people'); return; }
    const s = { p: null, retrying: {}, at: hhmm(), err: '' };
    host.innerHTML = '<div class="wiz"><p class="muted">Đang tải…</p></div>';

    function card(d) {
      const items = d.items || [];
      const total = items.length;
      const okN = items.filter((i) => i.state === 'ok').length;
      const waitN = total - okN;
      const st = ST[d.state] || ST.pending;
      const pillText = d.state === 'ok' ? 'Đủ ' + okN + '/' + total : (d.state === 'error' ? 'Có lỗi' : 'Đang chờ ' + waitN + ' việc');
      const rows = items.map((it) => {
        const x = ST[it.state] || ST.pending;
        return '<li class="dc-li"><span class="st st-' + x.cls + '">' + icon(x.icon, 18) + '</span><span class="lt">' + esc(it.label) + '</span>' +
          '<span class="st-' + x.cls + ' small">' + x.text + '</span>' +
          (it.message ? '<div class="dres-m small">' + esc(it.message) + '</div>' : '') +
          (it.detail ? '<details class="det dres-m"><summary>Chi tiết</summary><pre>' + esc(it.detail) + '</pre></details>' : '') + '</li>';
      }).join('');
      // Say "mất kết nối" only when the device really is offline (devices.changed keeps ACS.state current).
      const dev = ACS.state && (ACS.state.devices || []).find((x) => x.id === d.deviceId);
      const lead = (html) => '<p class="dc-note" style="background:#fff;border:0;padding:0;margin:0 18px 12px;color:var(--text-3);font-size:15px">' + html + '</p>';
      let pendingNote;
      if (dev && dev.authPaused) {
        pendingNote = lead('Máy này đang dừng vì sai mật khẩu nên chưa nhận được. App <strong>đã ghi nhớ</strong> và sẽ tự gửi khi mật khẩu đúng.') +
          '<div class="dc-note"><strong>Cách xử lý:</strong> mở tab “Máy chấm công &amp; cập nhật”, bấm “Sửa mật khẩu” ở ' + esc(d.deviceName.toLocaleLowerCase('vi')) + '.</div>';
      } else if (dev && dev.online) {
        pendingNote = lead('Máy đang nhận dữ liệu. App sẽ đọc lại từ máy để kiểm tra rồi báo “Đã nhận” — bạn không cần làm lại.') +
          ((d.items || []).some((i) => /chạy So khớp/.test(i.message || ''))
            ? '<div class="dc-note"><strong>Chưa có việc gửi cho máy này:</strong> chạy <a href="#/reconcile">So khớp 2 máy</a> để gửi.</div>' : '');
      } else {
        pendingNote = lead('Máy này đang mất kết nối nên chưa nhận được. App <strong>đã ghi nhớ</strong> và sẽ tự gửi khi máy có mạng lại — bạn không cần làm lại.') +
          '<div class="dc-note"><strong>Nếu chờ lâu:</strong> kiểm tra ' + esc(d.deviceName.toLocaleLowerCase('vi')) + ' có điện không và dây mạng có cắm chặt không.</div>';
      }
      const note = d.state === 'pending' ? pendingNote
        : (d.state === 'error' ? '<div class="dc-note"><strong>Cách xử lý:</strong> bấm “Thử lại ngay”. Nếu vẫn lỗi, mở trang người này để chụp lại hoặc xem chi tiết.</div>' : '');
      const busy = !!s.retrying[d.deviceId];
      const foot = d.state === 'ok'
        ? '<div class="dc-f">' + icon('shield', 16) + (s.wasWaiting && s.wasWaiting[d.deviceId] ? 'Máy có mạng lại, đã gửi và đọc lại để kiểm tra' : 'Đã đọc lại từ máy để chắc chắn máy đã lưu') + ' · ' + s.at + '</div>'
        : '<div class="dc-f dc-f-act">' + (busy
          ? '<button type="button" class="btn btn-xl is-busy" style="width:auto;height:44px;font-size:15px;padding:0 16px" aria-disabled="true" disabled>' + icon('spinner', 18).replace('class="ic"', 'class="ic xoay"') + 'Đang thử gửi…</button>'
          : '<button type="button" class="btn btn-primary" data-act="retry" data-dev="' + d.deviceId + '">' + icon('refresh', 18) + 'Thử lại ngay</button>') + '</div>';
      const pill = d.state === 'ok' ? 'background:var(--ok-bg);color:var(--ok)' : (d.state === 'error' ? 'background:var(--err-bg);color:var(--err)' : 'background:var(--wait-bg);color:var(--wait)');
      return '<section class="dc dc-' + st.cls + '"' + (d.state === 'ok' ? ' role="status"' : '') + '><div class="dc-h"><span class="devic devic-' + st.cls + '" style="width:40px;height:40px">' + icon('device', 20) + '</span>' +
        '<h2>' + esc(d.deviceName) + '</h2><span class="dc-pill" style="' + pill + '">' + esc(pillText) + '</span></div>' +
        '<ul class="dc-ul">' + rows + '</ul>' + note + foot + '</section>';
    }

    function draw() {
      const p = s.p;
      const nF = p.fingers.length;
      const fingerNames = p.fingers.map((f) => f.label.toLocaleLowerCase('vi')).join(', ');
      const sum = ['Mã NV ' + p.employeeNo, p.faceDataUrl ? '1 khuôn mặt' : 'chưa có khuôn mặt', nF ? nF + ' vân tay (' + fingerNames + ')' : 'chưa có vân tay'].join(' · ');
      host.innerHTML = '<div class="wiz">' +
        W.stepper(4, { 1: p.faceDataUrl ? 'Xong' : 'Bỏ qua', 2: nF ? 'Xong · ' + nF + ' ngón' : 'Bỏ qua', 3: 'Đã lưu' }) +
        '<div class="done-banner"><span class="done-ic">' + icon('check', 30, 2.4) + '</span><div style="flex:1 1 300px;min-width:0">' +
        '<h1>Đã đăng ký xong ' + esc(p.fullName) + '</h1><div class="sum">' + esc(sum) + '</div></div></div>' +
        (s.err ? W.errBox(s.err) : '') +
        '<div class="dcards">' + p.devices.map(card).join('') + '</div>' +
        '<div class="wfoot"><a class="btn btn-48" href="#/new/info">' + icon('addPerson', 18) + 'Thêm người khác</a>' +
        '<a class="btn btn-primary btn-next" href="#/people">Về danh sách</a></div></div>';
    }

    async function load() {
      try {
        const r = await ACS.bridge.call('people.get', { employeeNo: emp });
        if (!ctx.alive()) return;
        const before = s.p ? s.p.devices : [];
        s.wasWaiting = s.wasWaiting || {};
        r.devices.forEach((d) => { const o = before.find((x) => x.deviceId === d.deviceId); if (o && o.state !== 'ok' && d.state === 'ok') s.wasWaiting[d.deviceId] = true; });
        s.p = r; s.at = hhmm(); draw();
      } catch (e) {
        if (ctx.alive()) host.innerHTML = '<div class="wiz"><a class="back" href="#/people">' + icon('chevronLeft', 18) + 'Về danh sách</a>' + W.errBox(ACS.ui.errMessage(e)) + '</div>';
      }
    }

    on(host, 'click', '[data-act="retry"]', async (el) => {
      const id = +el.dataset.dev;
      if (s.retrying[id]) return;
      s.retrying[id] = true; s.err = ''; draw();
      try { await ACS.bridge.call('sync.retry', { deviceId: id }); }
      catch (e) { if (ctx.alive()) s.err = ACS.ui.errMessage(e); }
      if (!ctx.alive()) return;
      // Give the device a moment; person.changed will also refresh this screen by itself.
      setTimeout(() => { if (ctx.alive()) { delete s.retrying[id]; load(); } }, 1500);
    });

    const off = ACS.bridge.on('person.changed', (d) => { if (d && String(d.employeeNo) === String(emp)) load(); });
    const offState = ACS.onState(() => { if (s.p && ctx.alive()) draw(); });   // online / offline changes the note
    load();
    return () => { off(); offState(); };
  }

  ACS.screens = ACS.screens || {};
  ACS.screens['new/done'] = { render };
})();
