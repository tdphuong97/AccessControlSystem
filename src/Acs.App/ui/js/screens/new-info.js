// Wizard step 1 - Thông tin. #/new/info creates the person; #/new/info/:emp edits one that was just created (the "Quay lại" button).
(function () {
  const ACS = window.ACS;
  const { esc, on } = ACS.ui;
  const icon = ACS.icons.icon;
  const W = ACS.wizard;

  function render(host, params, ctx) {
    const emp = params.emp || null;
    const devs = W.currentDevices();
    const devNames = devs.map((d) => d.name);
    const s = { fullName: '', employeeNo: '', groupName: '', groupTyped: '', groupPicked: false, limited: false, validTo: '', err: '', field: '', busy: false, groups: [], ready: false };

    host.innerHTML = '<div class="wiz"><p class="muted">Đang tải…</p></div>';

    function draw() {
      const nDev = devs.length || 2;
      host.innerHTML = '<div class="wiz">' +
        '<a class="back" href="#/people">' + icon('chevronLeft', 18) + 'Về danh sách</a>' +
        '<div><h1 class="h1">' + (emp ? 'Sửa thông tin người mới' : 'Thêm người mới') + '</h1>' +
        '<p class="sub">4 bước, khoảng 2 phút. Người cần đăng ký nên đứng sẵn ở máy chấm công.</p></div>' +
        W.stepper(0) +
        '<div class="info-cols"><form class="info-form" id="f" novalidate>' +
        '<div><h2 class="h2-lg">Người này là ai?</h2><p class="sub">Chỉ bắt buộc họ tên. Mã nhân viên app tự điền số kế tiếp.</p></div>' +
        '<div><label class="field-lbl" for="ho-ten">Họ và tên <span class="req">*</span></label>' +
        '<input id="ho-ten" class="input in48 in-focus" type="text" maxlength="32" autocomplete="off" placeholder="Ví dụ: Nguyễn Văn An" value="' + esc(s.fullName) + '"' +
        (s.err && s.field === 'name' ? ' aria-invalid="true"' : '') + '>' +
        '<div class="hint">Tối đa 32 ký tự, máy chấm công không nhận tên dài hơn.</div></div>' +
        '<div class="frow2"><div><label class="field-lbl" for="ma-nv">Mã nhân viên</label>' +
        '<input id="ma-nv" class="input in48 in-mono" type="text" inputmode="numeric" autocomplete="off" value="' + esc(s.employeeNo) + '"' + (emp ? ' readonly' : '') + '>' +
        '<div class="hint">' + (emp ? 'Mã không đổi được sau khi tạo.' : 'Tự gợi ý — sửa nếu người này đã có mã.') + '</div></div>' +
        '<div><label class="field-lbl" for="nhom">Nhóm / công ty <span class="muted fw5" style="font-weight:400">(không bắt buộc)</span></label>' +
        ACS.ui.groupField('nhom', s.groupName, s.groups, 'input in48', s.groupTyped) + '</div></div>' +
        '<fieldset class="fieldset"><legend>Được ra vào đến khi nào?</legend><div class="radios">' +
        '<label class="radio' + (s.limited ? '' : ' on') + '"><input type="radio" name="han" value="open"' + (s.limited ? '' : ' checked') + '>' +
        '<span><span class="rt">Không thời hạn</span><span class="rs">Nhân viên chính thức</span></span></label>' +
        '<label class="radio' + (s.limited ? ' on' : '') + '"><input type="radio" name="han" value="limited"' + (s.limited ? ' checked' : '') + '>' +
        '<span><span class="rt">Đến một ngày</span><span class="rs">Thời vụ, khách, nhà thầu</span></span></label></div>' +
        (s.limited ? '<div style="margin-top:10px"><label class="field-lbl" for="han-den">Hết hạn vào ngày</label>' +
          '<input id="han-den" class="input in48" type="date" value="' + esc(s.validTo) + '"></div>' : '') + '</fieldset>' +
        '<div class="notice-ok">' + icon('checkCircle', 20) + '<div><div class="fw6" style="color:var(--ok-ink)">Được vào cả ' + nDev + ' máy</div>' +
        '<div class="small" style="color:#1F3A2C">' + esc(devNames.join(' · ') || 'Cả hai máy chấm công') + '. Người mới luôn được thêm vào tất cả máy, không cần chọn.</div></div></div>' +
        '<div id="err">' + W.errBox(s.err) + '</div></form>' +
        '<aside class="info-aside" aria-label="Các bước tiếp theo"><h2>Sau bước này</h2><ol>' +
        asideItem('camera', 'Chụp khuôn mặt', 'Bằng camera của máy chấm công, khoảng 10 giây.') +
        asideItem('fingerprint', 'Quét vân tay', 'Nên lấy 2 ngón, mỗi tay một ngón.') +
        asideItem('refresh', 'App tự gửi lên ' + nDev + ' máy', 'Và đọc lại từ máy để chắc chắn máy đã nhận.') + '</ol></aside></div>' +
        '<div class="wfoot"><a class="btn btn-48" href="#/people">Huỷ</a>' +
        '<button type="submit" form="f" class="btn btn-primary btn-next" id="go"' + (s.busy ? ' disabled' : '') + '>' +
        (s.busy ? 'Đang lưu…' : 'Tiếp: Chụp khuôn mặt' + icon('chevronRight', 18)) + '</button></div></div>';
      if (s.focus) { const el = host.querySelector(s.focus); if (el) el.focus(); s.focus = null; }
    }

    function asideItem(ic, t, sub) {
      return '<li><span class="aside-ic">' + icon(ic, 20) + '</span><span><span class="aside-t">' + esc(t) + '</span><span class="aside-s">' + esc(sub) + '</span></span></li>';
    }

    function readForm() {
      const q = (x) => host.querySelector(x);
      if (!q('#ho-ten')) return;
      s.fullName = q('#ho-ten').value; s.employeeNo = q('#ma-nv').value; s.groupName = q('#nhom').value; s.groupTyped = q('#nhom-new').value;
      const han = host.querySelector('input[name="han"]:checked');
      s.limited = !!han && han.value === 'limited';
      const to = q('#han-den'); if (to) s.validTo = to.value;
    }

    async function init() {
      try {
        const [list, nextNo, person] = await Promise.all([
          ACS.bridge.call('people.list', { filter: 'all', query: '' }),
          emp ? Promise.resolve(emp) : ACS.bridge.call('people.nextNo'),
          emp ? ACS.bridge.call('people.get', { employeeNo: emp }) : Promise.resolve(null),
        ]);
        if (!ctx.alive()) return;
        s.groups = ACS.fmt.groupList(list).map((g) => g.name);
        s.employeeNo = String(nextNo);
        if (person) { s.fullName = person.fullName; s.groupName = person.groupName || ''; s.groupPicked = true; s.limited = !!person.validTo; s.validTo = person.validTo || ''; }
        s.ready = true;
        s.focus = '#ho-ten';
        draw();
      } catch (e) {
        if (!ctx.alive()) return;
        host.innerHTML = '<div class="wiz"><a class="back" href="#/people">' + icon('chevronLeft', 18) + 'Về danh sách</a>' + W.errBox(ACS.ui.errMessage(e)) + '</div>';
      }
    }

    on(host, 'change', 'input[name="han"]', () => { readForm(); s.err = ''; draw(); });
    ACS.ui.wireGroup(host, 'nhom');
    on(host, 'change', '#nhom', () => { s.groupPicked = true; });
    // Names on site start with the company code ("ABC. Lan"): pick that group until the user picks one themselves.
    on(host, 'input', '#ho-ten', (el) => {
      const sel = host.querySelector('#nhom');
      if (s.groupPicked || !sel) return;
      const g = ACS.fmt.groupFromName(el.value);
      sel.value = (g && s.groups.find((x) => x.toUpperCase() === g)) || '';
    });

    host.addEventListener('submit', async (ev) => {
      ev.preventDefault();
      if (s.busy || !s.ready) return;
      readForm();
      const name = s.fullName.trim();
      const no = s.employeeNo.trim();
      if (!name) { s.err = 'Hãy nhập họ tên rồi bấm Tiếp.'; s.field = 'name'; s.focus = '#ho-ten'; draw(); return; }
      if (!no) { s.err = 'Hãy nhập mã nhân viên, hoặc bấm vào ô mã để dùng số app gợi ý.'; s.field = 'no'; s.focus = '#ma-nv'; draw(); return; }
      if (s.limited && !s.validTo) { s.err = 'Hãy chọn ngày hết hạn, hoặc chọn "Không thời hạn".'; s.field = 'to'; s.focus = '#han-den'; draw(); return; }
      s.err = ''; s.busy = true; draw();
      const groupName = ACS.ui.groupValue(host, 'nhom') || null;
      const validTo = s.limited ? s.validTo : null;
      try {
        if (emp) await ACS.bridge.call('people.update', { employeeNo: emp, fullName: name, groupName, validTo });
        else await ACS.bridge.call('people.create', { fullName: name, employeeNo: no, groupName, validTo });
        if (!ctx.alive()) return;
        ACS.router.go('new/face', emp || no);
      } catch (e) {
        if (!ctx.alive()) return;
        s.busy = false; s.err = ACS.ui.errMessage(e); s.field = ''; draw();
      }
    });

    init();
  }

  ACS.screens = ACS.screens || {};
  ACS.screens['new/info'] = { render };
})();
