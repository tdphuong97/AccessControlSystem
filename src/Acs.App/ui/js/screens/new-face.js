// Wizard step 2 - Khuôn mặt. Three ways in: the terminal's own camera (face.capture), the PC webcam or a picture file
// (both go through ACS.image.normalizeFace and face.save). States: wait -> busy -> done, with per-device results.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const W = ACS.wizard;
  const CAMERA = 'camera';

  function render(host, params, ctx) {
    const emp = params.emp;
    if (!emp) { ACS.router.go('new/info'); return; }
    const devs = W.currentDevices();
    const s = { p: null, tt: 'cho', busyText: '', img: null, devices: [], err: '', devId: W.defaultDevice(devs), busy: false, live: null, liveErr: '' };
    let webcamDialog = null;
    host.innerHTML = '<div class="wiz"><p class="muted">Đang tải…</p></div>';

    const devName = () => { const d = devs.find((x) => x.id === s.devId); return d ? d.name : 'máy chấm công'; };

    function stage() {
      if (s.tt === 'dang' && s.capturing && s.live) {
        return '<div class="stage-photo stage-live"><img id="live-img" alt="Hình trực tiếp từ camera của ' + esc(devName()) + ' trong lúc chụp" src="' + esc(s.live) + '">' +
          '<span class="badge-live"><i class="live-dot"></i>Đang chụp · ' + esc(devName()) + '</span>' +
          '<span class="live-cap" role="status">' + esc(s.busyText) + '</span></div>';
      }
      if (s.tt === 'dang') {
        return '<div class="stage stage-busy"><span class="stage-pulse nhip">' + icon(CAMERA, 52, 1.5) + '</span><div role="status">' +
          '<div class="scan-t" style="color:var(--accent-ink)">Đang chụp…</div><div class="scan-s">' + esc(s.busyText) + '</div></div></div>';
      }
      if (s.tt === 'xong' && s.img) {
        return '<div class="stage-photo"><img alt="Ảnh khuôn mặt vừa chụp" src="' + esc(s.img) + '">' +
          '<span class="badge-ok">' + icon('check', 16, 2.4) + 'Đã chụp · ảnh rõ nét</span></div>' + W.deviceRows(s.devices, 'Khuôn mặt');
      }
      if (s.live) {
        return '<div class="stage-photo stage-live"><img id="live-img" alt="Hình trực tiếp từ camera của ' + esc(devName()) + '" src="' + esc(s.live) + '">' +
          '<span class="badge-live"><i class="live-dot"></i>Trực tiếp · ' + esc(devName()) + '</span></div>' +
          '<p class="small muted live-note">Canh cho mặt người này nằm giữa khung, rồi bấm “Chụp ngay”.</p>';
      }
      return '<div class="stage stage-wait"><span class="stage-ic">' + icon('faceScan', 72, 1.2) + '</span>' +
        '<div class="fw6" style="font-size:16px">' + (s.liveErr ? 'Chưa xem được hình trực tiếp' : 'Đang mở camera của ' + esc(devName()) + '…') + '</div>' +
        '<div class="small muted" style="font-size:14px">' + (s.liveErr ? esc(s.liveErr) + ' Vẫn bấm “Chụp ngay” được.' : 'Ảnh chụp sẽ hiện ở đây.') + '</div></div>';
    }

    function draw() {
      const p = s.p, off = s.busy ? ' disabled' : '';
      const done = s.tt === 'xong';
      host.innerHTML = '<div class="wiz">' + W.personHead(p) + W.stepper(1) +
        '<div class="face-cols"><div class="face-left" id="stage">' + stage() + '</div><div class="face-right">' +
        '<div class="panel-main"><div class="panel-h"><h2>Chụp bằng camera máy chấm công</h2><span class="pill pill-new" style="font-size:13px;padding:2px 10px">Nên dùng</span></div>' +
        '<div><label class="field-lbl" for="may-chup">Chụp ở máy</label>' + W.deviceSelect('may-chup', devs, s.devId).replace('<select', '<select' + off) + '</div>' +
        (done ? '<button type="button" class="btn btn-xl btn-outline-xl" data-act="capture"' + off + '>' + icon('refresh', 20) + 'Chụp lại</button>'
          : '<button type="button" class="btn btn-primary btn-xl' + (s.busy ? ' is-busy' : '') + '" data-act="capture"' + off + '>' +
            icon(CAMERA, 22) + (s.busy ? 'Đang chụp…' : 'Chụp ngay') + '</button>') +
        '<div id="err">' + W.errDiag(s.err, s.devId) + '</div>' +
        '<p class="small" style="margin:0;font-size:14px;color:var(--text-2)">Mời người này đứng trước máy, cách khoảng nửa mét, nhìn thẳng vào camera. Máy tự chụp và app gửi ảnh lên cả ' + (devs.length || 2) + ' máy.</p></div>' +
        '<div class="or">hoặc</div><div class="alt-btns">' +
        '<button type="button" class="btn" data-act="webcam"' + off + '>' + icon('webcam', 18) + 'Webcam máy tính</button>' +
        '<button type="button" class="btn" data-act="file"' + off + '>' + icon('image', 18) + 'Chọn ảnh có sẵn</button></div>' +
        '<input type="file" id="f-file" accept="image/*" hidden>' +
        '<div class="tips"><div class="fw6" style="margin-bottom:8px">Để máy nhận diện tốt</div><ul>' +
        ['Đủ sáng, không đứng ngược sáng', 'Bỏ khẩu trang, kính râm, mũ', 'Nhìn thẳng, không nghiêng đầu'].map((t) => '<li>' + icon('check', 16) + esc(t) + '</li>').join('') + '</ul></div>' +
        '</div></div>' +
        '<div class="wfoot"><a class="btn btn-48" href="' + ACS.router.href('new/info', emp) + '">' + icon('chevronLeft', 18) + 'Quay lại</a><div class="wfoot-r">' +
        (done ? '' : '<a class="btn btn-48 btn-link" href="' + ACS.router.href('new/finger', emp) + '">Bỏ qua bước này</a>') +
        '<a class="btn btn-primary btn-next" href="' + ACS.router.href('new/finger', emp) + '">Tiếp: Lấy vân tay' + icon('chevronRight', 18) + '</a></div></div></div>';
    }

    // Runs one capture/save call: shows the busy stage, then the result or an inline error (the previous photo stays).
    async function run(busyText, fn) {
      if (s.busy) return;
      s.busy = true; s.err = ''; s.tt = 'dang'; s.busyText = busyText; draw();
      try {
        const r = await fn();
        if (!ctx.alive()) return;
        s.img = r.imageDataUrl; s.devices = r.devices || []; s.tt = 'xong';
      } catch (e) {
        if (!ctx.alive()) return;
        s.tt = s.img ? 'xong' : 'cho'; s.err = ACS.ui.errMessage(e);
      }
      s.busy = false; s.capturing = false; draw();
    }

    const save = (dataUrl, source) => ACS.bridge.call('face.save', { employeeNo: emp, imageDataUrl: dataUrl, source });

    async function openWebcam() {
      if (s.busy) return;
      if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        s.err = 'Máy tính này không có webcam dùng được. Hãy chụp bằng camera máy chấm công hoặc chọn ảnh có sẵn.'; draw(); return;
      }
      let stream;
      s.busy = true; s.err = ''; draw();
      try { stream = await navigator.mediaDevices.getUserMedia({ video: { width: { ideal: 1280 }, height: { ideal: 960 }, facingMode: 'user' }, audio: false }); }
      catch (e) {
        s.busy = false;
        if (ctx.alive()) { s.err = 'Không mở được webcam. Hãy cho phép app dùng camera, hoặc chọn ảnh có sẵn.'; draw(); }
        return;
      }
      if (!ctx.alive()) { stream.getTracks().forEach((t) => t.stop()); return; }
      s.busy = false; draw();
      const html = '<div class="dlg-body"><h2 class="dlg-title">Chụp bằng webcam</h2>' +
        '<video class="cam" id="cam" autoplay playsinline muted></video>' +
        '<p class="dlg-text small" style="margin-top:10px">Mời người này nhìn thẳng vào webcam, rồi bấm “Chụp”.</p>' +
        '<div class="dlg-actions"><button type="button" class="btn" data-no>Thôi</button><button type="button" class="btn btn-primary" data-snap>' + icon(CAMERA, 18) + 'Chụp</button></div></div>';
      const shot = await ACS.ui.dialog(html, {
        onOpen(d) {
          webcamDialog = d;
          const v = d.querySelector('#cam');
          v.srcObject = stream;
          d.addEventListener('close', () => stream.getTracks().forEach((t) => t.stop()));
          d.querySelector('[data-no]').addEventListener('click', () => d.finish(null));
          d.querySelector('[data-snap]').addEventListener('click', () => {
            if (!v.videoWidth) return;
            const c = document.createElement('canvas');
            c.width = v.videoWidth; c.height = v.videoHeight;
            c.getContext('2d').drawImage(v, 0, 0);
            d.finish(c);
          });
        },
      });
      webcamDialog = null;
      if (!shot || !ctx.alive()) return;
      run('Đang xử lý ảnh và gửi lên máy…', async () => save(await ACS.image.normalizeFace(shot), 'webcam'));
    }

    function pickFile(file) {
      if (!file) return;
      run('Đang xử lý ảnh và gửi lên máy…', async () => {
        const url = URL.createObjectURL(file);
        try {
          const img = await new Promise((ok, no) => {
            const im = new Image();
            im.onload = () => ok(im);
            im.onerror = () => no(new Error('Không đọc được ảnh này. Hãy chọn ảnh JPG hoặc PNG khác.'));
            im.src = url;
          });
          return await save(await ACS.image.normalizeFace(img), 'file');
        } finally { URL.revokeObjectURL(url); }
      });
    }

    // Live view: frames from the chosen device's camera while nothing is captured yet (about 2-3 per second). The host
    // answers busy while the device is capturing, so the loop never delays a capture.
    const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
    const redrawStage = () => { const el = host.querySelector('#stage'); if (el) el.innerHTML = stage(); };
    let liveOn = false;
    async function liveLoop() {
      if (liveOn) return;
      liveOn = true;
      try {
        while (ctx.alive()) {
          // Before the capture and during it (the device camera keeps answering while it captures).
          const wanted = s.tt === 'cho' ? !s.busy : s.tt === 'dang' && s.capturing;
          if (!wanted || document.hidden || !s.devId) { await sleep(400); continue; }
          const dev = s.devId;
          try {
            const r = await ACS.bridge.call('face.preview', { deviceId: dev });
            if (!ctx.alive()) return;
            if (r && r.imageDataUrl && (s.tt === 'cho' || s.capturing) && dev === s.devId) {
              const img = host.querySelector('#live-img');
              s.live = r.imageDataUrl; s.liveErr = '';
              if (img) img.src = r.imageDataUrl; else redrawStage();
            }
            await sleep(150);
          } catch (e) {
            if (!ctx.alive()) return;
            if (dev === s.devId && s.tt === 'cho') { s.live = null; s.liveErr = ACS.ui.errMessage(e); redrawStage(); }
            await sleep(3000);
          }
        }
      } finally { liveOn = false; }
    }

    async function refreshDevices() {
      if (s.tt !== 'xong' || s.busy) return;
      try { const d = await ACS.bridge.call('sync.person', { employeeNo: emp }); if (ctx.alive()) { s.devices = d; const el = host.querySelector('#stage'); if (el) el.innerHTML = stage(); } }
      catch (e) { /* the next event or the next screen shows the real state */ }
    }

    on(host, 'click', '[data-act]', (el) => {
      const act = el.dataset.act;
      if (act === 'capture') { if (!s.busy) s.capturing = true; run('Mời nhìn thẳng vào camera của ' + devName(), () => ACS.bridge.call('face.capture', { employeeNo: emp, deviceId: s.devId })); }
      else if (act === 'webcam') openWebcam();
      else if (act === 'file') { const f = host.querySelector('#f-file'); if (f) f.click(); }
    });
    on(host, 'change', '#may-chup', (el) => { s.devId = +el.value; s.live = null; s.liveErr = ''; if (s.tt === 'cho') redrawStage(); });
    on(host, 'change', '#f-file', (el) => { const f = el.files && el.files[0]; el.value = ''; pickFile(f); });

    const off = ACS.bridge.on('person.changed', (d) => { if (d && String(d.employeeNo) === String(emp)) refreshDevices(); });

    ACS.bridge.call('people.get', { employeeNo: emp }).then((p) => {
      if (!ctx.alive()) return;
      s.p = p; draw(); liveLoop();
    }).catch((e) => {
      if (ctx.alive()) host.innerHTML = '<div class="wiz"><a class="back" href="#/people">' + icon('chevronLeft', 18) + 'Về danh sách</a>' + W.errBox(ACS.ui.errMessage(e)) + '</div>';
    });

    return () => { off(); if (webcamDialog && webcamDialog.open) webcamDialog.close(); };
  }

  ACS.screens = ACS.screens || {};
  ACS.screens['new/face'] = { render };
})();
