// Mock host used when the page is opened outside WebView2 (design checks, screenshots). Answers every C3
// command with believable sample data. All people and devices are invented.
// Query flags: ?err (one failed sync), ?locked (a stale locked person), ?noupdate, ?authpause, ?firstrun (no devices yet), ?empty, ?nobackup (no backup password), ?plansame (reconcile finds nothing), ?needrec (app.state.needsReconcile), ?importwarn (import could not read everyone)
(function () {
  const ACS = (window.ACS = window.ACS || {});
  const F = () => ACS.fmt;
  const q = location.search;
  const DAY = 86400000;
  const ago = (ms) => new Date(Date.now() - ms).toISOString();
  const day = (n) => new Date(Date.now() - n * DAY).toISOString().slice(0, 10);

  const devices = [
    { id: 1, name: 'Máy tầng trệt', host: '192.168.1.201', username: 'admin', doorNo: 1, isCaptureDefault: true, isFingerReader: true,
      model: 'DS-K1T341AM', online: true, authPaused: false, lastSeen: ago(20000), pending: 0, failed: 0, message: null },
    { id: 2, name: 'Máy tầng 1', host: '192.168.1.202', username: 'admin', doorNo: 1, isCaptureDefault: false, isFingerReader: true,
      model: 'DS-K1T341AM', online: false, authPaused: false, lastSeen: ago(5 * 60000 + 20000), pending: 1, failed: 0,
      message: 'Không kết nối được tới máy. Kiểm tra dây mạng và nguồn của máy.' },
  ];
  if (/authpause/.test(q)) {
    Object.assign(devices[1], { authPaused: true, online: true, message: 'Sai mật khẩu hoặc máy đang khoá tạm (khoảng 30 phút). Kiểm tra mật khẩu ở tab "Máy chấm công & cập nhật" (nút Sửa mật khẩu).' });
  }

  // fingers: [fingerId, quality]; wait: device names still waiting; created: days ago
  const people = [
    { employeeNo: '1137', fullName: 'Nguyễn Văn An', initials: 'NA', groupName: 'Kỹ thuật', hasFace: true, fingers: [[7, 86], [2, 79]], wait: [], created: 1 },
    { employeeNo: '1136', fullName: 'Trần Thị Bình', initials: 'TB', groupName: 'Kế toán', hasFace: true, fingers: [[7, 91], [2, 74]], wait: ['Máy tầng 1'], created: 3 },
    { employeeNo: '1135', fullName: 'Lê Hoàng Cường', initials: 'LC', groupName: 'Kinh doanh', hasFace: true, fingers: [[3, 68]], wait: [], created: 40 },
    { employeeNo: '1134', fullName: 'Phạm Thu Dung', initials: 'PD', groupName: 'Kế toán', hasFace: false, fingers: [[6, 83], [1, 88]], wait: [], created: 52 },
    { employeeNo: '1133', fullName: 'Hoàng Minh Đức', initials: 'HĐ', groupName: 'Kho', hasFace: true, fingers: [], wait: [], created: 90 },
    { employeeNo: '1132', fullName: 'Võ Thị Hà', initials: 'VH', groupName: 'Nhân sự', hasFace: true, fingers: [[7, 77], [8, 72]], wait: [], created: 120, error: /err/.test(q) },
    { employeeNo: '1131', fullName: 'Đặng Quốc Huy', initials: 'ĐH', groupName: 'Kho', hasFace: true, fingers: [[2, 81], [7, 85]], wait: [], created: 150 },
  ];
  people.forEach((p) => { p.validFrom = day(p.created); p.validTo = null; p.locked = false; });
  if (/locked/.test(q)) {
    people.push({ employeeNo: '1090', fullName: 'Bùi Văn Khánh', initials: 'BK', groupName: 'Kho', hasFace: true, fingers: [[7, 80]], wait: [], created: 400,
      validFrom: day(400), validTo: null, locked: true });
  }
  if (/empty|firstrun/.test(q)) people.length = 0;
  if (/firstrun/.test(q)) devices.length = 0;

  const syncOf = (p) => (p.error ? 'error' : (p.wait.length ? 'pending' : 'ok'));
  const row = (p) => ({ employeeNo: p.employeeNo, fullName: p.fullName, initials: p.initials || F().initials(p.fullName), groupName: p.groupName,
    hasFace: p.hasFace, fingerCount: p.fingers.length, sync: syncOf(p), waitingDevices: p.wait, isNew: p.created < 7, locked: p.locked });

  // Placeholder portrait (abstract silhouette), not a real photo.
  const portrait = 'data:image/svg+xml;utf8,' + encodeURIComponent(
    '<svg xmlns="http://www.w3.org/2000/svg" width="240" height="300" viewBox="0 0 240 300"><rect width="240" height="300" fill="#DCE6F7"/>' +
    '<circle cx="120" cy="115" r="52" fill="#9DB6E3"/><path d="M28 300c0-62 40-98 92-98s92 36 92 98z" fill="#9DB6E3"/></svg>');

  function deviceSync(p) {
    return devices.map((d) => {
      const waits = p.wait.includes(d.name);
      const err = p.error && d.id === 2;
      const st = err ? 'error' : (waits ? 'pending' : 'ok');
      const msg = err ? 'Máy từ chối ảnh mặt: ảnh quá mờ. Hãy chụp lại mặt ở nơi đủ sáng.'
        : (waits ? 'Máy đang mất kết nối. App sẽ tự gửi khi máy có mạng lại.' : null);
      const items = [{ label: 'Thông tin', state: waits ? 'pending' : 'ok', message: null, detail: null }];
      if (p.hasFace) {
        items.push({ label: 'Khuôn mặt', state: err ? 'error' : (waits ? 'pending' : 'ok'), message: msg,
          detail: err ? 'HTTP 200 {"statusCode":4,"subStatusCode":"faceQuality","statusString":"Invalid Content"}' : null });
      }
      p.fingers.forEach((f) => items.push({ label: 'Vân tay ' + F().fingerLabel(f[0]), state: waits ? 'pending' : 'ok', message: null, detail: null }));
      return { deviceId: d.id, deviceName: d.name, state: st, items };
    });
  }
  function detail(p) {
    return { employeeNo: p.employeeNo, fullName: p.fullName, groupName: p.groupName, validFrom: p.validFrom, validTo: p.validTo, locked: p.locked,
      faceDataUrl: p.hasFace ? portrait : null,
      fingers: p.fingers.map((f) => ({ fingerId: f[0], label: F().fingerLabel(f[0]), quality: f[1] })), devices: deviceSync(p) };
  }
  const find = (a) => people.find((p) => p.employeeNo === String(a.employeeNo));
  const need = (a) => {
    const p = find(a);
    if (!p) throw new Error('Không tìm thấy người này. Hãy quay lại danh sách và thử lại.');
    return p;
  };
  const lowSeen = {};
  const wait = (ms) => new Promise((r) => setTimeout(r, ms));

  let settings = { autoCheckUpdates: true, startWithWindows: false };
  // True after a new device or a restore, until reconcile.apply (or a plan with nothing to send).
  let needsReconcile = /needrec/.test(q);
  let extra = null;
  const bak = { hasPassword: !/nobackup|firstrun/.test(q), lastAt: ago(6 * 3600000), files: [
    { name: 'acs-2026-11-06.acsbak', at: ago(6 * 3600000), size: 1843200 }, { name: 'acs-2026-11-05.acsbak', at: ago(30 * 3600000), size: 1802240 },
    { name: 'acs-truoc-cap-nhat-0.9.0.acsbak', at: ago(80 * 3600000), size: 1790000 }] };
  if (/nobackup|firstrun/.test(q)) { bak.lastAt = null; bak.files = []; }
  const update = /noupdate/.test(q) ? null : {
    version: '1.1.0', notes: 'Sửa lỗi báo xong khi máy chưa nhận vân tay. Cập nhật mất khoảng 30 giây, dữ liệu giữ nguyên.', sizeMb: 48.2 };

  const filters = {
    all: () => true, noFace: (p) => !p.hasFace && !p.locked, noFinger: (p) => !p.fingers.length && !p.locked,
    notSynced: (p) => syncOf(p) !== 'ok' && !p.locked, locked: (p) => p.locked,
  };
  const norm = (s) => String(s).normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/gi, 'd').toLowerCase();

  const handlers = {
    'app.state': () => ({ version: '0.9.0', firstRun: devices.length === 0, devices, update, pendingJobs: devices.reduce((n, d) => n + d.pending, 0),
      failedJobs: people.filter((p) => p.error).length, staleLocked: people.filter((p) => p.locked && p.created > 180).length,
      needsReconcile: needsReconcile && devices.length > 0 }),
    'people.list': (a) => people.filter(filters[a.filter || 'all'] || filters.all)
      .filter((p) => !a.query || norm(p.fullName + ' ' + p.employeeNo).includes(norm(a.query))).map(row),
    'people.nextNo': () => String((people.length ? Math.max(...people.map((p) => +p.employeeNo)) : 1000) + 1),
    'people.create': (a) => {
      if (!a.fullName || !a.fullName.trim()) throw new Error('Hãy nhập họ tên rồi bấm Tiếp tục.');
      if (people.some((p) => p.employeeNo === String(a.employeeNo))) throw new Error('Mã nhân viên này đã có. Hãy chọn mã khác.');
      const p = { employeeNo: String(a.employeeNo), fullName: a.fullName.trim(), groupName: a.groupName || null, hasFace: false, fingers: [], wait: devices.filter((d) => !d.online).map((d) => d.name),
        created: 0, validFrom: day(0), validTo: a.validTo || null, locked: false };
      people.unshift(p);
      return detail(p);
    },
    'people.get': (a) => detail(need(a)),
    'people.update': (a) => {
      const p = need(a);
      p.fullName = a.fullName; p.groupName = a.groupName || null; p.validTo = a.validTo || null; p.initials = F().initials(a.fullName);
      return detail(p);
    },
    'people.lock': (a) => { const p = need(a); p.locked = true; return detail(p); },
    'people.unlock': (a) => { const p = need(a); p.locked = false; return detail(p); },
    'people.delete': (a) => { people.splice(people.indexOf(need(a)), 1); return null; },
    'face.capture': async (a) => {
      const p = need(a);
      await wait(1500);
      const d = devices.find((x) => x.id === a.deviceId);
      if (d && !d.online) throw new Error(d.name + ' đang mất kết nối nên chưa chụp được. Hãy chọn máy khác hoặc kiểm tra dây mạng và nguồn của máy.');
      p.hasFace = true; return { imageDataUrl: portrait, devices: deviceSync(p) }; },
    'face.save': (a) => { const p = need(a); p.hasFace = true; return { imageDataUrl: a.imageDataUrl, devices: deviceSync(p) }; },
    'finger.capture': async (a) => {
      const p = need(a);
      await wait(1500);
      const d = devices.find((x) => x.id === a.deviceId);
      if (d && !d.online) throw new Error(d.name + ' đang mất kết nối nên chưa quét được. Hãy chọn máy khác hoặc kiểm tra dây mạng và nguồn của máy.');
      // Little fingers fail once with a low-quality scan, like a dry or scratched finger would.
      if ((a.fingerId === 5 || a.fingerId === 10) && !lowSeen[a.fingerId]) {
        lowSeen[a.fingerId] = true;
        throw new Error('Vân tay quét chưa rõ (chất lượng 31/100, cần từ 40). Hãy lau khô ngón tay, đặt chắc lên đầu đọc rồi bấm quét lại.');
      }
      p.fingers = p.fingers.filter((f) => f[0] !== a.fingerId).concat([[a.fingerId, 82]]);
      return { fingerId: a.fingerId, label: F().fingerLabel(a.fingerId), quality: 82, devices: deviceSync(p) };
    },
    'finger.delete': (a) => { const p = need(a); p.fingers = p.fingers.filter((f) => f[0] !== a.fingerId); return detail(p); },
    'sync.person': (a) => deviceSync(need(a)),
    'sync.retry': (a) => {
      // The offline device comes back: waiting people turn green a moment later, announced by person.changed.
      const targets = devices.filter((d) => a.deviceId == null || d.id === a.deviceId);
      setTimeout(() => {
        targets.forEach((d) => { d.online = true; d.authPaused = false; d.pending = 0; d.message = null; });
        const names = targets.map((d) => d.name);
        people.forEach((p) => {
          if (p.wait.some((n) => names.includes(n))) {
            p.wait = p.wait.filter((n) => !names.includes(n));
            ACS.bridge.emit('person.changed', { employeeNo: p.employeeNo });
          }
        });
        ACS.bridge.emit('devices.changed', JSON.parse(JSON.stringify(devices)));
      }, 1200);
      return null;
    },
    'devices.list': () => devices,
    'devices.save': (a) => {
      if (!a.name || !a.host) throw new Error('Hãy nhập tên và địa chỉ của máy.');
      let d = devices.find((x) => x.id === a.id);
      if (d) {
        Object.assign(d, { name: a.name, host: a.host, username: a.username, doorNo: a.doorNo, isCaptureDefault: a.isCaptureDefault, isFingerReader: a.isFingerReader });
        if (d.authPaused && a.password) { d.authPaused = false; d.message = null; }   // an empty password keeps the stored one
      } else {
        d = { id: Math.max(0, ...devices.map((x) => x.id)) + 1, name: a.name, host: a.host, username: a.username, doorNo: a.doorNo,
          isCaptureDefault: a.isCaptureDefault, isFingerReader: a.isFingerReader, model: null, online: a.host !== '192.168.1.202', authPaused: false,
          lastSeen: null, pending: 0, failed: 0, message: null };
        devices.push(d);
        if (people.length || devices.length > 1) needsReconcile = true;
      }
      ACS.bridge.emit('devices.changed', JSON.parse(JSON.stringify(devices)));
      return d;
    },
    'devices.test': async (a) => {
      await wait(700);
      const d = devices.find((x) => x.id === a.id);
      if (!d) throw new Error('Không tìm thấy máy này. Hãy mở lại tab "Máy chấm công & cập nhật".');
      if (d.authPaused) return { ok: false, model: null, firmware: null, users: 0, canCaptureFace: false, canCaptureFinger: false,
        message: 'Máy báo sai mật khẩu. Bấm "Sửa mật khẩu", nhập lại rồi kiểm tra lại. Nếu máy đã tự khoá địa chỉ này thì chờ khoảng 30 phút.' };
      return d.online
        ? { ok: true, model: 'DS-K1T341AM', firmware: 'V4.30.012', users: people.length || 136, canCaptureFace: true, canCaptureFinger: true, message: 'Kết nối tốt.' }
        : { ok: false, model: null, firmware: null, users: 0, canCaptureFace: false, canCaptureFinger: false,
          message: 'Không kết nối được tới ' + d.host + '. Kiểm tra dây mạng và nguồn của máy, rồi bấm kiểm tra lại.' };
    },
    'devices.delete': (a) => {
      const i = devices.findIndex((x) => x.id === a.id);
      if (i >= 0) devices.splice(i, 1);
      ACS.bridge.emit('devices.changed', JSON.parse(JSON.stringify(devices)));
      return null;
    },
    'reconcile.plan': async () => {
      ACS.bridge.emit('reconcile.progress', { message: 'Đang đọc Máy tầng trệt…' });
      await wait(900);
      ACS.bridge.emit('reconcile.progress', { message: 'Đang đọc Máy tầng 1…' });
      await wait(900);
      if (/plansame/.test(q)) { needsReconcile = false; return { devices: devices.map((d) => ({ deviceId: d.id, deviceName: d.name, items: [], strangers: [] })) }; }
      const d1 = devices[0] || { id: 1, name: 'Máy tầng trệt' }, d2 = devices[1] || { id: 2, name: 'Máy tầng 1' };
      return { devices: [
        { deviceId: d1.id, deviceName: d1.name, items: [], strangers: [{ deviceId: d1.id, employeeNo: '2001', name: 'Mai Thị Lan', faceCount: 1, fingerCount: 1 }] },
        { deviceId: d2.id, deviceName: d2.name,
          items: [
            { employeeNo: '1137', fullName: 'Nguyễn Văn An', kind: 'MissingPerson', fingers: [] },
            { employeeNo: '1136', fullName: 'Trần Thị Bình', kind: 'InfoMismatch', fingers: [] },
            { employeeNo: '1135', fullName: 'Lê Hoàng Cường', kind: 'MissingFace', fingers: [] },
            { employeeNo: '1134', fullName: 'Phạm Minh Đức', kind: 'MissingFingers', fingers: [7, 2] },
            { employeeNo: '1133', fullName: 'Hoàng Thị Em', kind: 'ExtraFingers', fingers: [3] },
            { employeeNo: '1090', fullName: 'Bùi Văn Khánh', kind: 'LockedButEnabled', fingers: [] }],
          strangers: [{ deviceId: d2.id, employeeNo: '2002', name: 'Đỗ Quang Hưng', faceCount: 1, fingerCount: 0 },
            { deviceId: d2.id, employeeNo: '2003', name: 'Vũ Thị Hoa', faceCount: 0, fingerCount: 2 }] }] };
    },
    'reconcile.apply': async () => {
      ACS.bridge.emit('reconcile.progress', { message: 'Đang tạo việc gửi cho Máy tầng 1…' });
      await wait(1200);
      needsReconcile = false;
      return null;
    },
    'import.start': async () => {
      const names = ['Nguyễn Văn An', 'Trần Thị Bình', 'Lê Hoàng Cường', 'Phạm Minh Đức', 'Hoàng Thị Em'];
      const total = 24;
      for (let i = 1; i <= total; i += 4) {
        ACS.bridge.emit('import.progress', { done: i, total, currentName: names[(i >> 2) % names.length] });
        await wait(350);
      }
      ACS.bridge.emit('import.progress', { done: total, total, currentName: '' });
      if (/importwarn/.test(q)) ACS.bridge.emit('import.progress', { done: total, total, currentName: 'Không đọc được đủ dữ liệu của 2 người. Xem Nhật ký; chạy lại Lấy dữ liệu sau nếu cần.' });
      if (devices.length > 1) needsReconcile = true;
      return { people: total, withFace: 21, facesMissing: 3, fingers: 38 };
    },
    'backup.status': () => ({ hasPassword: bak.hasPassword, lastAt: bak.lastAt, files: bak.files, extraFolder: extra }),
    'backup.setPassword': (a) => {
      if (!a.password || a.password.length < 8) throw new Error('Mật khẩu sao lưu cần ít nhất 8 ký tự.');
      bak.hasPassword = true; return null;
    },
    'backup.now': async () => {
      await wait(900);
      if (!bak.hasPassword) throw new Error('Hãy đặt mật khẩu sao lưu trước.');
      bak.lastAt = ago(0);
      bak.files.unshift({ name: 'acs-' + day(0) + '-' + String(bak.files.length + 1) + '.acsbak', at: ago(0), size: 1900000 });
      return null;
    },
    'backup.setExtraFolder': (a) => {
      if (a.folder && !/^[A-Za-z]:[\\/]|^\\\\/.test(a.folder)) throw new Error('Không dùng được thư mục này. Hãy nhập đường dẫn đầy đủ, ví dụ E:\\SaoLuuACS.');
      extra = a.folder || null; return null;
    },
    // Another folder (new PC, USB): any full path lists two invented files; "E:\\Trong" is an empty folder.
    'backup.listFolder': async (a) => {
      await wait(400);
      const dir = String(a.folder || '').trim();
      if (!/^[A-Za-z]:[\\/]|^\\\\/.test(dir)) throw new Error('Hãy nhập đường dẫn đầy đủ của thư mục có file .acsbak (ví dụ E:\\SaoLuuACS hoặc \\\\may-chu\\SaoLuu).');
      if (/trong$/i.test(dir)) return [];
      return [{ name: 'ACS-20261105-210000-hang-ngay.acsbak', at: ago(30 * 3600000), size: 1802240 },
        { name: 'ACS-20261101-090000-thu-cong.acsbak', at: ago(5 * 86400000), size: 1750000 }];
    },
    'backup.restore': async (a) => {
      await wait(900);
      if (!a.name && !/\.acsbak$/i.test(String(a.path || ''))) throw new Error('Hãy chọn một file sao lưu .acsbak bằng đường dẫn đầy đủ.');
      if (a.password === 'sai') throw new Error('Mật khẩu sao lưu chưa đúng. Hãy nhập lại; nếu đã quên mật khẩu thì không mở được bản sao lưu này.');
      needsReconcile = true;   // the real app restarts and then asks for So khớp
      return null;
    },
    'update.check': async () => { await wait(600); return update; },
    'update.install': async () => {
      for (let i = 1; i <= 10; i++) setTimeout(() => ACS.bridge.emit('update.progress', { fraction: i / 10 }), i * 500);
      return null;
    },
    'settings.get': () => settings,
    'settings.set': (a) => (settings = Object.assign({}, settings, a)),
    'log.list': (a) => {
      const rows = [
        { at: ago(300000), deviceId: 2, deviceName: 'Máy tầng 1', employeeNo: '1136', action: 'Gửi vân tay Trỏ trái', ok: false, message: 'Không kết nối được tới máy. Kiểm tra dây mạng và nguồn của máy.' },
        { at: ago(310000), deviceId: 2, deviceName: 'Máy tầng 1', employeeNo: '1136', action: 'Gửi khuôn mặt', ok: false, message: 'Máy từ chối ảnh mặt: ảnh quá mờ. Hãy chụp lại mặt ở nơi đủ sáng.' },
        { at: ago(3600000), deviceId: 1, deviceName: 'Máy tầng trệt', employeeNo: '1137', action: 'Gửi khuôn mặt', ok: true, message: '' },
        { at: ago(3610000), deviceId: 1, deviceName: 'Máy tầng trệt', employeeNo: '1137', action: 'Gửi thông tin người', ok: true, message: '' },
        { at: ago(7200000), deviceId: 2, deviceName: 'Máy tầng 1', employeeNo: '1135', action: 'Đọc lại để kiểm tra vân tay Giữa phải', ok: true, message: 'Máy đã lưu đúng.' },
        { at: ago(26 * 3600000), deviceId: 0, deviceName: '', employeeNo: '', action: 'Sao lưu tự động', ok: true, message: 'acs-2026-11-05.acsbak' },
        { at: ago(50 * 3600000), deviceId: 2, deviceName: 'Máy tầng 1', employeeNo: '', action: 'Đăng nhập máy', ok: false, message: 'Sai mật khẩu. Vào "Máy chấm công & cập nhật" để nhập lại mật khẩu.' },
      ];
      return rows.filter((r) => !a.deviceId || r.deviceId === a.deviceId).filter((r) => !a.employeeNo || r.employeeNo === a.employeeNo);
    },
  };

  ACS.mock = {
    async call(cmd, args) {
      await wait(30);
      const h = handlers[cmd];
      if (!h) throw new Error('Lệnh chưa được hỗ trợ: ' + cmd);
      const r = await h(args);
      return r === undefined ? null : JSON.parse(JSON.stringify(r));
    },
  };
})();
