// Pure helpers shared by the screens (browser: ACS.fmt, Node: module.exports for tests).
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else { root.ACS = root.ACS || {}; root.ACS.fmt = api; }
})(typeof self !== 'undefined' ? self : this, function () {
  const FINGERS = ['Cái', 'Trỏ', 'Giữa', 'Áp út', 'Út'];

  // First letter of the first word + first letter of the last word, uppercase, diacritics kept.
  function initials(name) {
    const words = String(name || '').trim().split(/\s+/).filter(Boolean);
    if (!words.length) return '';
    const up = (w) => Array.from(w)[0].toLocaleUpperCase('vi');
    if (words.length === 1) return up(words[0]);
    const first = words[0], last = words[words.length - 1];
    return up(first) + up(last);
  }

  // Finger ids 1-5 right hand (thumb to little), 6-10 left hand.
  function fingerLabel(id) {
    if (!Number.isInteger(id) || id < 1 || id > 10) return 'Ngón ' + id;
    return FINGERS[(id - 1) % 5] + (id <= 5 ? ' phải' : ' trái');
  }

  function durationText(seconds) {
    if (seconds < 60) return 'dưới 1 phút';
    if (seconds < 3600) return Math.floor(seconds / 60) + ' phút';
    if (seconds < 86400) return Math.floor(seconds / 3600) + ' giờ';
    return Math.floor(seconds / 86400) + ' ngày';
  }

  function timeAgo(iso, now) {
    if (!iso) return 'chưa rõ';
    const t = Date.parse(iso);
    if (Number.isNaN(t)) return 'chưa rõ';
    const s = ((now === undefined ? Date.now() : now) - t) / 1000;
    if (s < 60) return 'vừa xong';
    return durationText(s) + ' trước';
  }

  function plural(n, one) { return n + ' ' + one; }

  function fmtDate(d) {
    if (!d) return '';
    const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(d);
    return m ? m[3] + '/' + m[2] + '/' + m[1] : d;
  }

  // People per page on the people screen; 0 = everyone on one page.
  const PAGE_SIZES = [20, 50, 100, 0];
  function pageSize(saved) {
    const n = Number(saved);
    return saved != null && saved !== '' && PAGE_SIZES.includes(n) ? n : 50;
  }

  // Page `page` (1-based) of `total` rows: slice [from, to). A page past the end shows the last one.
  function paginate(total, size, page) {
    const pages = size > 0 ? Math.max(1, Math.ceil(total / size)) : 1;
    const p = Math.min(Math.max(1, Math.floor(page) || 1), pages);
    return size > 0 ? { page: p, pages, from: (p - 1) * size, to: Math.min(total, p * size) } : { page: 1, pages: 1, from: 0, to: total };
  }

  // On site names start with the company code ("ABC. Lan"): the code before the first dot is the group (as GroupRules in the core).
  function groupFromName(name) {
    const m = /^\s*([\p{L}\p{Nd}]{1,6})\s*\./u.exec(name || '');
    return m ? m[1].toUpperCase() : null;
  }

  const NO_GROUP = '-';
  function groupList(rows) {
    const n = new Map();
    for (const r of rows) if (r.groupName) n.set(r.groupName, (n.get(r.groupName) || 0) + 1);
    return Array.from(n, ([name, count]) => ({ name, count })).sort((a, b) => a.name.localeCompare(b.name, 'vi'));
  }
  function byGroup(rows, group) {
    if (!group) return rows;
    return rows.filter((r) => (group === NO_GROUP ? !r.groupName : r.groupName === group));
  }

  // People list order: the host sorts by name; "Mã NV" compares the numbers as numbers (2 < 10 < 0012 < 100),
  // any code that is not all digits after them.
  const SORTS = [['name', 'Tên (A–Z)'], ['noAsc', 'Mã NV tăng dần'], ['noDesc', 'Mã NV giảm dần (mới nhất trước)']];
  function sortMode(saved) { return SORTS.some(([k]) => k === saved) ? saved : 'name'; }
  function compareNo(a, b) {
    const na = /^\d+$/.test(a), nb = /^\d+$/.test(b);
    if (na && nb) { const x = BigInt(a), y = BigInt(b); return x < y ? -1 : x > y ? 1 : (a < b ? -1 : a > b ? 1 : 0); }
    if (na !== nb) return na ? -1 : 1;
    return a.localeCompare(b, 'vi', { numeric: true });
  }
  function sortRows(rows, mode) {
    if (mode === 'noAsc') return rows.slice().sort((x, y) => compareNo(x.employeeNo, y.employeeNo));
    if (mode === 'noDesc') return rows.slice().sort((x, y) => compareNo(y.employeeNo, x.employeeNo));
    return rows;
  }

  return { initials, fingerLabel, durationText, timeAgo, plural, fmtDate, PAGE_SIZES, pageSize, paginate, groupFromName, NO_GROUP, groupList, byGroup,
    SORTS, sortMode, sortRows };
});
