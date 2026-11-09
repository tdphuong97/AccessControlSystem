// Pure helpers for the "Dữ liệu chung" section (browser: ACS.cloud, Node: module.exports for tests).
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else { root.ACS = root.ACS || {}; root.ACS.cloud = api; }
})(typeof self !== 'undefined' ? self : this, function () {
  const hhmm = (iso) => { const m = /T(\d{2}):(\d{2})/.exec(iso || ''); return m ? m[1] + ':' + m[2] : ''; };

  function statusLine(st) {
    if (!st || !st.enabled) return st && st.connected ? 'Dữ liệu chung đang tắt trên máy này. Máy dùng riêng dữ liệu của nó.' : 'Chưa kết nối dữ liệu chung.';
    if (!st.online) return st.pending > 0 ? 'Mất kết nối Internet, thay đổi đang giữ trên máy (' + st.pending + ').' : 'Mất kết nối Internet. Phần mềm vẫn chạy bằng dữ liệu trên máy.';
    if (!st.lastSyncAt) return 'Đang đồng bộ…';
    return 'Đã đồng bộ lúc ' + hhmm(st.lastSyncAt) + (st.pending > 0 ? ' · còn ' + st.pending + ' thay đổi chưa gửi' : '');
  }

  function senderLine(st) {
    if (!st || !st.enabled) return '';
    if (st.isSender) return 'Máy này đang gửi xuống máy chấm công.';
    return st.senderName ? 'Đang gửi xuống máy chấm công: ' + st.senderName + '.' : '';
  }

  function codeLooksValid(code) { return /^acs1:[a-z0-9.-]+:[A-Za-z0-9_-]+$/i.test(String(code || '').trim()); }

  function passwordProblem(pw, again, creating) {
    if (creating) {
      if (!pw || pw.length < 8) return 'Mật khẩu dữ liệu chung cần ít nhất 8 ký tự.';
      if (pw !== again) return 'Hai lần nhập mật khẩu chưa giống nhau. Hãy nhập lại cho khớp.';
      return '';
    }
    return pw ? '' : 'Hãy nhập mật khẩu dữ liệu chung.';
  }

  return { statusLine, senderLine, codeLooksValid, passwordProblem };
});
