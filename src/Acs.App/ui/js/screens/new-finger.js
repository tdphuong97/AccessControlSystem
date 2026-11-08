// Wizard step 3 - Vân tay. Two-hand picker (ids 1-5 right hand thumb..little, 6-10 left), scan ring, per-device result.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const W = ACS.wizard;
  const F = ACS.fmt;
  const HEIGHT = { 1: 60, 2: 82, 3: 94, 4: 86, 5: 70 };
  const NAME = { 1: 'Cái', 2: 'Trỏ', 3: 'Giữa', 4: 'Áp út', 5: 'Út' };
  const LEFT = [10, 9, 8, 7, 6]; // little finger on the left edge, thumb next to the right hand
  const RIGHT = [1, 2, 3, 4, 5];
  const lower = (id) => F.fingerLabel(id).toLocaleLowerCase('vi');

  function qualityText(q) {
    if (q == null) return 'Đã lấy vân tay';
    return (q >= 70 ? 'Chất lượng tốt' : 'Chất lượng đạt') + ' (' + q + '/100)';
  }

  function render(host, params, ctx) {
    const emp = params.emp;
    if (!emp) { ACS.router.go('new/info'); return; }
    const devs = W.currentDevices();
    const s = { p: null, got: [], chon: null, tt: 'cho', last: null, err: '', devId: W.defaultDevice(devs), busy: false };
    host.innerHTML = '<div class="wiz"><p class="muted">Đang tải…</p></div>';

    const devName = () => { const d = devs.find((x) => x.id === s.devId); return d ? d.name : 'máy chấm công'; };

    function fingerBtn(id) {
      const n = ((id - 1) % 5) + 1;
      const got = s.got.includes(id), sel = s.chon === id && !got;
      return '<button type="button" class="fing' + (got ? ' got' : (sel ? ' sel' : '')) + '" data-fid="' + id + '" style="height:' + HEIGHT[n] + 'px"' +
        (sel ? ' aria-pressed="true"' : (got ? '' : ' aria-pressed="false"')) +
        ' aria-label="Ngón ' + esc(lower(id)) + (got ? ' — đã lấy' : '') + '"' + (s.busy ? ' disabled' : '') + '>' +
        (got ? icon('check', 16, 2.4) : '') + NAME[n] + '</button>';
    }

    // Result sentence for the last scan: which devices got it, which are waiting, which refused.
    function resultText() {
      const l = s.last;
      const item = 'Vân tay ' + l.label;
      const ok = [], wait = [], bad = [];
      (l.devices || []).forEach((d) => {
        const it = W.itemState(d, item);
        (it.state === 'ok' ? ok : (it.state === 'error' ? bad : wait)).push(d.deviceName);
      });
      const parts = [qualityText(l.quality) + '.'];
      if (ok.length) parts.push('Đã gửi lên ' + ok.join(', ') + '.');
      if (wait.length) parts.push(wait.join(', ') + ' sẽ nhận khi có mạng.');
      if (bad.length) parts.push(bad.join(', ') + ' báo lỗi, xem trang người này để thử lại.');
      return parts.join(' ');
    }

    function draw() {
      const coChon = s.chon != null;
      const cho = s.tt === 'cho' || (s.tt === 'xong' && coChon);
      const dang = s.tt === 'dang';
      const xong = s.tt === 'xong' && !coChon;
      const ring = dang ? '<span class="ring ring-on nhip">' + icon('scan', 76, 1.3) + '</span>'
        : (xong ? '<span class="ring ring-ok">' + icon('check', 72, 2) + '</span>' : '<span class="ring">' + icon('scan', 76, 1.3) + '</span>');
      let status;
      if (dang) status = '<div class="scan-t" style="color:var(--accent-ink)">Mời đặt ngón ' + esc(lower(s.chon)) + ' lên đầu đọc</div><div class="scan-s">ở ' + esc(devName()) + ' — giữ yên đến khi máy kêu bíp</div>';
      else if (xong) status = '<div class="scan-t" style="color:var(--ok-ink)">Xong ngón ' + esc(s.last.label.toLocaleLowerCase('vi')) + '</div><div class="scan-s">' + esc(resultText()) + '</div>';
      else if (cho && coChon) status = '<div class="scan-t">Sẵn sàng lấy ngón ' + esc(lower(s.chon)) + '</div><div class="scan-s">Bấm “Bắt đầu quét”, rồi mời người này đặt ngón lên đầu đọc.</div>';
      else status = '<div class="scan-t">Chọn một ngón ở bên trái</div><div class="scan-s">Ngón đã lấy có dấu tích màu xanh.</div>';
      const btn = dang ? '<button type="button" class="btn btn-primary btn-xl is-busy" aria-disabled="true" disabled>Đang chờ ngón tay…</button>'
        : (coChon ? '<button type="button" class="btn btn-primary btn-xl" data-act="scan">' + icon('scan', 22) + 'Bắt đầu quét</button>'
          : '<button type="button" class="btn btn-xl is-off" aria-disabled="true" disabled>Chọn ngón trước</button>');
      const gotList = s.got.length ? s.got.slice().sort((a, b) => a - b).map(lower).join(', ') : 'chưa có';
      host.innerHTML = '<div class="wiz">' + W.personHead(s.p) +
        W.stepper(2, { 1: s.p.faceDataUrl ? 'Xong' : 'Bỏ qua' }) +
        '<div class="fin-cols"><div class="fin-pick"><div><h2 class="h2" style="font-size:18px;font-weight:700">Chọn ngón cần lấy</h2>' +
        '<p class="sub">Nên lấy 2 ngón, mỗi tay một ngón, phòng khi một ngón bị trầy xước.</p></div>' +
        '<div class="hands"><div class="hand"><div class="hand-t">Tay trái</div><div class="hand-row">' + LEFT.map(fingerBtn).join('') + '</div></div>' +
        '<div class="hand"><div class="hand-t">Tay phải</div><div class="hand-row">' + RIGHT.map(fingerBtn).join('') + '</div></div></div>' +
        '<div class="legend"><span><i class="lg" style="background:var(--accent)"></i>Đang chọn</span>' +
        '<span><i class="lg" style="background:var(--ok-bg);border:2px solid var(--ok)"></i>Đã lấy</span>' +
        '<span><i class="lg" style="background:var(--soft);border:1px solid #A9B4C2"></i>Chưa lấy</span></div>' +
        '<div class="got-box"><strong>Đã lấy ' + s.got.length + ' ngón:</strong> ' + esc(gotList) + '</div></div>' +
        '<div class="fin-scan"><div><label class="field-lbl" for="may-quet">Quét ở máy</label>' + W.deviceSelect('may-quet', devs, s.devId).replace('<select', '<select' + (s.busy ? ' disabled' : '')) + '</div>' +
        '<div class="scan-mid">' + ring + '<div class="scan-status" role="status">' + status + '</div></div>' +
        '<div id="err">' + W.errDiag(s.err, s.devId) + '</div>' + btn + '</div></div>' +
        '<div class="wfoot"><a class="btn btn-48" href="' + ACS.router.href('new/face', emp) + '">' + icon('chevronLeft', 18) + 'Quay lại</a>' +
        '<a class="btn btn-primary btn-next" href="' + ACS.router.href('new/done', emp) + '">Tiếp: Hoàn tất' + icon('chevronRight', 18) + '</a></div></div>';
    }

    async function scan() {
      if (s.busy || s.chon == null) return;
      const fid = s.chon;
      s.busy = true; s.err = ''; s.tt = 'dang'; draw();
      try {
        const r = await ACS.bridge.call('finger.capture', { employeeNo: emp, deviceId: s.devId, fingerId: fid });
        if (!ctx.alive()) return;
        if (!s.got.includes(r.fingerId)) s.got.push(r.fingerId);
        s.last = { id: r.fingerId, label: r.label || F.fingerLabel(r.fingerId), quality: r.quality, devices: r.devices };
        s.chon = null; s.tt = 'xong';
      } catch (e) {
        if (!ctx.alive()) return;
        s.tt = 'cho'; s.err = ACS.ui.errMessage(e);
      }
      s.busy = false; draw();
    }

    async function refresh() {
      if (!s.last || s.busy) return;
      try { const d = await ACS.bridge.call('sync.person', { employeeNo: emp }); if (ctx.alive() && !s.busy) { s.last.devices = d; draw(); } }
      catch (e) { /* ignore: the next event refreshes it */ }
    }

    on(host, 'click', '[data-fid]', (el) => {
      if (s.busy) return;
      const id = +el.dataset.fid;
      if (s.got.includes(id)) return;
      s.chon = id; s.tt = 'cho'; s.err = ''; draw();
    });
    on(host, 'click', '[data-act="scan"]', scan);
    on(host, 'change', '#may-quet', (el) => { s.devId = +el.value; });

    const off = ACS.bridge.on('person.changed', (d) => { if (d && String(d.employeeNo) === String(emp)) refresh(); });

    ACS.bridge.call('people.get', { employeeNo: emp }).then((p) => {
      if (!ctx.alive()) return;
      s.p = p; s.got = p.fingers.map((f) => f.fingerId); draw();
    }).catch((e) => {
      if (ctx.alive()) host.innerHTML = '<div class="wiz"><a class="back" href="#/people">' + icon('chevronLeft', 18) + 'Về danh sách</a>' + W.errBox(ACS.ui.errMessage(e)) + '</div>';
    });

    return off;
  }

  ACS.screens = ACS.screens || {};
  ACS.screens['new/finger'] = { render };
})();
