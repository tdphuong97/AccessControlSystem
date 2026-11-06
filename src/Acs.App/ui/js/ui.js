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

  ACS.ui = { esc, on, dialog, confirm: confirmBox, toast, errMessage };
})();
