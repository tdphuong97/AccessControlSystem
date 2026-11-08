// Small DOM helpers shared by every screen: escaping, modal dialogs, toasts, delegated clicks.
(function () {
  const ACS = (window.ACS = window.ACS || {});

  const esc = (s) => String(s == null ? '' : s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

  // Delegated listener: on(root, 'click', '[data-act]', (el, ev) => ...)
  function on(root, type, selector, fn) {
    root.addEventListener(type, (ev) => {
      const el = ev.target.closest(selector);
      if (el && root.contains(el)) fn(el, ev);
    });
  }

  // Opens a modal <dialog>; the promise resolves with whatever d.finish(value) was given (null when dismissed).
  function dialog(html, opts) {
    return new Promise((resolve) => {
      const d = document.createElement('dialog');
      d.className = 'dlg';
      d.innerHTML = html;
      document.body.appendChild(d);
      let result = null;
      d.addEventListener('close', () => { d.remove(); resolve(result); });
      d.addEventListener('click', (ev) => { if (ev.target === d) d.close(); });
      d.finish = (value) => { result = value; d.close(); };
      d.showModal();
      if (opts && opts.onOpen) opts.onOpen(d);
    });
  }

  function confirmBox(o) {
    const html = '<div class="dlg-body">' +
      '<h2 class="dlg-title">' + esc(o.title) + '</h2>' +
      (o.body ? '<p class="dlg-text">' + esc(o.body) + '</p>' : '') +
      '<div class="dlg-actions"><button type="button" class="btn" data-no>' + esc(o.cancelText || 'Thôi') + '</button>' +
      '<button type="button" class="btn ' + (o.danger ? 'btn-danger' : 'btn-primary') + '" data-yes>' + esc(o.okText || 'Đồng ý') + '</button></div></div>';
    return dialog(html, {
      onOpen(d) {
        d.querySelector('[data-no]').addEventListener('click', () => d.finish(false));
        d.querySelector('[data-yes]').addEventListener('click', () => d.finish(true));
        d.querySelector('[data-no]').focus();
      },
    }).then((r) => !!r);
  }

  let toastTimer = 0;
  function toast(message, kind) {
    let box = document.getElementById('toast');
    if (!box) { box = document.createElement('div'); box.id = 'toast'; box.setAttribute('role', 'status'); document.body.appendChild(box); }
    box.className = 'toast ' + (kind === 'error' ? 'toast-err' : 'toast-ok');
    box.textContent = message;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { box.className = 'toast hidden'; }, kind === 'error' ? 8000 : 3500);
  }

  function errMessage(e) {
    return (e && e.message) || 'Có lỗi xảy ra. Hãy thử lại, nếu vẫn lỗi thì mở Nhật ký để xem chi tiết.';
  }

  // "Nhóm / công ty": pick one of the groups people already have, or "+ Nhóm mới…" to type one (shown in the box below).
  const NEW_GROUP = '__new';
  function groupField(id, value, names, cls, typed) {
    const list = value && value !== NEW_GROUP && !names.includes(value) ? names.concat(value) : names;
    const opt = (v, t) => '<option value="' + esc(v) + '"' + (v === (value || '') ? ' selected' : '') + '>' + esc(t) + '</option>';
    return '<select id="' + id + '" class="' + cls + '">' + opt('', '— Không thuộc nhóm nào —') + list.map((n) => opt(n, n)).join('') +
      opt(NEW_GROUP, '+ Nhóm mới…') + '</select>' +
      '<input id="' + id + '-new" class="' + cls + ' group-new" type="text" maxlength="40" autocomplete="off" placeholder="Gõ tên nhóm mới, ví dụ: ABC"' +
      ' aria-label="Tên nhóm mới" value="' + esc(typed || '') + '"' + (value === NEW_GROUP ? '' : ' hidden') + '>';
  }
  function wireGroup(root, id) {
    root.addEventListener('change', (ev) => {
      if (ev.target.id !== id) return;
      const box = root.querySelector('#' + id + '-new');
      box.hidden = ev.target.value !== NEW_GROUP;
      if (!box.hidden) box.focus();
    });
  }
  function groupValue(root, id) {
    const sel = root.querySelector('#' + id);
    if (!sel) return '';
    return (sel.value === NEW_GROUP ? root.querySelector('#' + id + '-new').value : sel.value).trim();
  }

  ACS.ui = { esc, on, dialog, confirm: confirmBox, toast, errMessage, NEW_GROUP, groupField, wireGroup, groupValue };
})();
