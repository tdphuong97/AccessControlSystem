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

  return { initials, fingerLabel, durationText, timeAgo, plural, fmtDate };
});
