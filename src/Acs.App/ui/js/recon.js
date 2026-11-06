// Pure helpers for settings / reconcile / setup / log (browser: ACS.recon, Node: module.exports for tests).
(function (root, factory) {
  const fmt = (typeof module === 'object' && module.exports) ? require('./format.js') : (root.ACS && root.ACS.fmt);
  const api = factory(fmt);
  if (typeof module === 'object' && module.exports) module.exports = api;
  else { root.ACS = root.ACS || {}; root.ACS.recon = api; }
})(typeof self !== 'undefined' ? self : this, function (F) {
  const CHOICES = ['skip', 'import', 'remove'];
  const CHOICE_LABEL = { skip: 'Bỏ qua', import: 'Nhập vào app', remove: 'Gỡ khỏi máy' };
  const IMPORT_EXPLAIN = 'Nhập vào app: đọc khuôn mặt và vân tay từ máy về app, rồi gửi cho các máy còn lại.';
  const kindOf = (k) => String(k == null ? '' : k).replace(/[^a-z]/gi, '').toLowerCase();
  const fingerList = (ids) => (ids || []).map((i) => F.fingerLabel(i).toLocaleLowerCase('vi')).join(', ');

  // One DiffItem -> one plain Vietnamese sentence saying what the app will do. Nothing here deletes anything.
  function describeItem(item) {
    const who = (item.fullName || 'Người này') + ' (mã ' + item.employeeNo + ')';
    switch (kindOf(item.kind)) {
      case 'missingperson': return who + ' chưa có trên máy này. App sẽ thêm vào.';
      case 'infomismatch': return who + ' có tên hoặc thời hạn khác với app. App sẽ sửa trên máy cho giống app.';
      case 'missingface': return who + ' thiếu khuôn mặt trên máy. App sẽ gửi lại khuôn mặt.';
      case 'missingfingers': return who + ' thiếu vân tay ' + (fingerList(item.fingers) || 'trên máy') + '. App sẽ gửi lại.';
      case 'extrafingers': return who + ' có thêm vân tay ' + (fingerList(item.fingers) || 'trên máy') + ' mà app chưa có. App chỉ báo, không tự xoá.';
      case 'lockedbutenabled': return who + ' đã khoá trong app nhưng máy vẫn cho ra vào. App sẽ gỡ hoặc tắt người này trên máy (dữ liệu vẫn giữ trong app).';
      default: return who + ': có điểm chưa khớp với app.';
    }
  }

  const SUMMARY = [
    ['missingperson', (n) => 'thiếu ' + n + ' người'],
    ['infomismatch', (n) => n + ' người sai thông tin'],
    ['missingface', (n) => n + ' người thiếu khuôn mặt'],
    ['missingfingers', (n) => n + ' người thiếu vân tay'],
    ['extrafingers', (n) => n + ' người có vân tay thừa'],
    ['lockedbutenabled', (n) => n + ' người đã khoá nhưng máy vẫn cho vào'],
  ];

  // "Thiếu 3 người, 2 người thiếu vân tay, 1 người lạ" or "Đã khớp".
  function summarizeDevice(d) {
    const items = d.items || [];
    const parts = [];
    SUMMARY.forEach(([k, f]) => { const n = items.filter((i) => kindOf(i.kind) === k).length; if (n) parts.push(f(n)); });
    const s = (d.strangers || []).length;
    if (s) parts.push(s + ' người lạ');
    if (!parts.length) return 'Đã khớp';
    const t = parts.join(', ');
    return t.charAt(0).toLocaleUpperCase('vi') + t.slice(1);
  }

  const strangerKey = (s) => s.deviceId + ':' + s.employeeNo;

  // The `strangers` argument of reconcile.apply: every stranger gets a key, unknown choices fall back to "skip".
  function buildStrangers(plan, choices) {
    const out = {};
    ((plan && plan.devices) || []).forEach((d) => (d.strangers || []).forEach((s) => {
      const c = choices && choices[strangerKey(s)];
      out[strangerKey(s)] = CHOICES.includes(c) ? c : 'skip';
    }));
    return out;
  }

  // [{deviceName, n}] of strangers that will be removed from a device (for the confirm dialog).
  function removals(plan, choices) {
    const res = [];
    ((plan && plan.devices) || []).forEach((d) => {
      const n = (d.strangers || []).filter((s) => (choices || {})[strangerKey(s)] === 'remove').length;
      if (n) res.push({ deviceName: d.deviceName, n });
    });
    return res;
  }

  // True when applying would do nothing (no diff items and every stranger skipped).
  function nothingToDo(plan, choices) {
    const c = choices || {};
    return !((plan && plan.devices) || []).some((d) => (d.items || []).some((i) => kindOf(i.kind) !== 'extrafingers') ||
      (d.strangers || []).some((s) => c[strangerKey(s)] && c[strangerKey(s)] !== 'skip'));
  }

  // Items of one device split by what the app will do: todo (add/fix), locked (locked but still enabled), info (report only).
  function splitItems(items) {
    const out = { todo: [], locked: [], info: [] };
    (items || []).forEach((i) => { const k = kindOf(i.kind); (k === 'lockedbutenabled' ? out.locked : (k === 'extrafingers' ? out.info : out.todo)).push(i); });
    return out;
  }

  // Locked-but-enabled people the apply will remove or switch off, for the confirm summary.
  function lockedCount(plan) {
    return ((plan && plan.devices) || []).reduce((n, d) => n + splitItems(d.items).locked.length, 0);
  }

  function planTotals(plan) {
    const devs = (plan && plan.devices) || [];
    return { items: devs.reduce((n, d) => n + (d.items || []).length, 0), strangers: devs.reduce((n, d) => n + (d.strangers || []).length, 0) };
  }

  // Backup password rules: at least 8 characters, typed twice the same. Returns '' when fine.
  function backupPasswordProblem(pw, again) {
    if (!pw || pw.length < 8) return 'Mật khẩu sao lưu cần ít nhất 8 ký tự. Hãy nhập dài hơn.';
    if (pw !== again) return 'Hai lần nhập mật khẩu chưa giống nhau. Hãy nhập lại cho khớp.';
    return '';
  }

  function fmtSize(bytes) {
    const b = Number(bytes) || 0;
    if (b < 1024) return b + ' B';
    if (b < 1024 * 1024) return Math.round(b / 1024) + ' KB';
    return (b / 1024 / 1024).toFixed(1).replace('.', ',') + ' MB';
  }

  const p2 = (n) => String(n).padStart(2, '0');
  // Local time "06/11 09:05" (adds the year when it is not the current one).
  function fmtDateTime(iso, now) {
    const t = Date.parse(iso);
    if (Number.isNaN(t)) return '';
    const d = new Date(t), cur = new Date(now === undefined ? Date.now() : now);
    return p2(d.getDate()) + '/' + p2(d.getMonth() + 1) + (d.getFullYear() === cur.getFullYear() ? '' : '/' + d.getFullYear()) +
      ' ' + p2(d.getHours()) + ':' + p2(d.getMinutes());
  }

  const norm = (s) => String(s == null ? '' : s).normalize('NFD').replace(/[̀-ͯ]/g, '').replace(/đ/gi, 'd').toLowerCase();

  // Client-side filtering of log.list rows: onlyErrors, free text over every field (diacritics ignored).
  function filterLog(rows, o) {
    o = o || {};
    const q = norm((o.q || '').trim());
    return (rows || []).filter((r) => {
      if (o.onlyErrors && r.ok) return false;
      if (!q) return true;
      return norm([r.deviceName, r.employeeNo, r.action, r.message].join(' ')).includes(q);
    });
  }

  // Device form values -> devices.save args. Empty password is sent as '' (the host keeps the stored one).
  function deviceArgs(v, id) {
    const a = { name: String(v.name || '').trim(), host: String(v.host || '').trim(), username: String(v.username || '').trim(),
      password: v.password || '', doorNo: /^\s*\d+\s*$/.test(String(v.doorNo)) ? parseInt(v.doorNo, 10) : 0, isCaptureDefault: !!v.isCaptureDefault, isFingerReader: !!v.isFingerReader };
    if (id != null) a.id = id;
    return a;
  }

  // '' when valid; otherwise a Vietnamese message that says what to fix.
  function deviceProblem(a, isNew) {
    if (!a.name) return 'Hãy đặt tên cho máy, ví dụ "Máy tầng trệt".';
    if (!a.host) return 'Hãy nhập địa chỉ của máy, ví dụ 192.168.1.201.';
    if (/[\s/]/.test(a.host) || /^https?:/i.test(a.host)) return 'Địa chỉ máy chỉ gồm số IP hoặc tên máy, không có "http://" hay dấu cách.';
    if (!Number.isInteger(a.doorNo) || a.doorNo < 1) return 'Số cửa phải là số nguyên từ 1 trở lên, thường là 1.';
    if (!a.username) return 'Hãy nhập tên tài khoản của máy (thường là admin).';
    if (isNew && !a.password) return 'Hãy nhập mật khẩu của máy.';
    return '';
  }

  return { CHOICES, CHOICE_LABEL, IMPORT_EXPLAIN, describeItem, summarizeDevice, strangerKey, buildStrangers, removals, nothingToDo, planTotals,
    splitItems, lockedCount, backupPasswordProblem, fmtSize, fmtDateTime, filterLog, deviceArgs, deviceProblem };
});
