// Thùng rác: people deleted with "Xoá hẳn", kept with face and fingers until restored or removed for good.
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const R = ACS.recon;

  function render(host, params, ctx) {
    const s = { rows: null, err: '', busy: {} };
    host.innerHTML = '<div class="pad stack"><div class="pagehead"><div><h1 class="h1">Thùng rác</h1>' +
      '<p class="sub">Người đã xoá hẳn: đã gỡ khỏi máy chấm công, app còn giữ thông tin, ảnh mặt và vân tay. ' +
      'Khôi phục để đưa lại vào app và gửi lại xuống máy. Thùng rác giữ tới khi bạn xoá vĩnh viễn.</p></div>' +
      '<a class="btn" href="#/people">' + icon('chevronLeft', 18) + 'Về danh sách</a></div>' +
      '<div id="tr-err"></div><div class="tablebox"><div id="tr-list" role="status"><div class="empty-row">Đang tải…</div></div>' +
      '<div class="tfoot"><span class="sub" id="tr-note"></span><div id="tr-empty"></div></div></div></div>';
    const $ = (id) => host.querySelector('#' + id);

    function row(t) {
      const what = [t.hasFace ? 'có ảnh mặt' : 'chưa có ảnh mặt', t.fingerCount ? t.fingerCount + ' vân tay' : 'chưa có vân tay'].join(' · ');
      const busy = s.busy[t.id];
      return '<div class="trow trash-row"><div class="who"><span class="avatar">' + esc(ACS.fmt.initials(t.fullName)) + '</span>' +
        '<div class="who-t"><div class="fw6">' + esc(t.fullName) + '</div><div class="mono small muted">Mã NV ' + esc(t.employeeNo) +
        (t.groupName ? ' · ' + esc(t.groupName) : '') + '</div></div></div>' +
        '<div class="c" data-l="Đã xoá">' + esc(R.fmtDateTime(t.deletedAt)) + '<div class="small muted">' + esc(what) + '</div>' +
        (t.blocked ? '<div class="small st-wait">' + esc(t.blocked) + '</div>' : '') + '</div>' +
        '<div class="c open rowwrap"><button type="button" class="btn btn-sm btn-primary" data-act="restore" data-id="' + t.id + '"' + (busy ? ' disabled' : '') + '>' +
        icon('refresh', 16) + 'Khôi phục</button>' +
        '<button type="button" class="btn btn-sm btn-danger-o" data-act="purge" data-id="' + t.id + '"' + (busy ? ' disabled' : '') + '>' + icon('trash', 16) + 'Xoá vĩnh viễn</button></div></div>';
    }

    function draw() {
      $('tr-err').innerHTML = s.err ? '<div class="alert alert-err" role="alert">' + icon('alertCircle', 18) + '<span>' + esc(s.err) + '</span></div>' : '';
      if (!s.rows) return;
      $('tr-list').innerHTML = s.rows.length ? s.rows.map(row).join('') : '<div class="empty-row">Thùng rác trống.</div>';
      $('tr-note').textContent = s.rows.length ? s.rows.length + ' người trong thùng rác' : '';
      $('tr-empty').innerHTML = s.rows.length ? '<button type="button" class="btn btn-sm btn-danger-o" data-act="empty">' + icon('trash', 16) + 'Dọn sạch thùng rác</button>' : '';
    }

    async function load() {
      try { const r = await ACS.bridge.call('trash.list'); if (!ctx.alive()) return; s.rows = r; s.err = ''; }
      catch (e) { if (!ctx.alive()) return; s.err = ACS.ui.errMessage(e); s.rows = s.rows || []; }
      draw();
    }

    // The old number is taken (or still being removed): restore under another one, suggested by the app.
    async function askNumber(t) {
      let next = '';
      try { next = String(await ACS.bridge.call('people.nextNo')); } catch (e) { /* the box stays empty */ }
      const html = '<form class="dlg-body" novalidate><h2 class="dlg-title">Khôi phục ' + esc(t.fullName) + ' với mã khác</h2>' +
        '<p class="dlg-text">' + esc(t.blocked || '') + '</p>' +
        '<label class="lbl" for="tr-no">Mã nhân viên mới</label><input id="tr-no" class="input in-mono" inputmode="numeric" autocomplete="off" value="' + esc(next) + '">' +
        '<div class="alert alert-err" id="tr-no-err" hidden></div>' +
        '<div class="dlg-actions"><button type="button" class="btn" data-no>Thôi</button><button type="submit" class="btn btn-primary">Khôi phục</button></div></form>';
      return ACS.ui.dialog(html, {
        onOpen(d) {
          const q = (x) => d.querySelector(x);
          q('[data-no]').addEventListener('click', () => d.finish(null));
          q('form').addEventListener('submit', async (ev) => {
            ev.preventDefault();
            try { d.finish(await ACS.bridge.call('trash.restore', { id: t.id, employeeNo: q('#tr-no').value.trim() })); }
            catch (e) { const b = q('#tr-no-err'); b.hidden = false; b.textContent = ACS.ui.errMessage(e); }
          });
          q('#tr-no').focus();
        },
      });
    }

    on(host, 'click', '[data-act]', async (el) => {
      const act = el.dataset.act;
      const t = s.rows && s.rows.find((x) => String(x.id) === el.dataset.id);
      try {
        if (act === 'restore' && t) {
          s.busy[t.id] = true; draw();
          const p = t.blocked ? await askNumber(t) : await ACS.bridge.call('trash.restore', { id: t.id });
          if (p) ACS.ui.toast('Đã khôi phục ' + p.fullName + ' (mã ' + p.employeeNo + '). App đang gửi lại xuống máy.');
        } else if (act === 'purge' && t) {
          const ok = await ACS.ui.confirm({ title: 'Xoá vĩnh viễn ' + t.fullName + '?', body: 'Xoá luôn thông tin, ảnh mặt và vân tay khỏi app. Không khôi phục lại được.', okText: 'Xoá vĩnh viễn', danger: true });
          if (ok) { await ACS.bridge.call('trash.delete', { id: t.id }); ACS.ui.toast('Đã xoá vĩnh viễn ' + t.fullName + '.'); }
        } else if (act === 'empty') {
          const ok = await ACS.ui.confirm({ title: 'Dọn sạch thùng rác?', body: 'Xoá vĩnh viễn cả ' + s.rows.length + ' người trong thùng rác, kể cả ảnh mặt và vân tay. Không khôi phục lại được.', okText: 'Dọn sạch', danger: true });
          if (ok) { const n = await ACS.bridge.call('trash.empty'); ACS.ui.toast('Đã xoá vĩnh viễn ' + n + ' người.'); }
        }
      } catch (e) { if (ctx.alive()) ACS.ui.toast(ACS.ui.errMessage(e), 'error'); }
      if (t) delete s.busy[t.id];
      if (ctx.alive()) load();
    });

    load();
  }

  ACS.screens = ACS.screens || {};
  ACS.screens.trash = { render };
})();
