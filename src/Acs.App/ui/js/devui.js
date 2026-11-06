// Device form and connection-test result, shared by the settings screen and the first-run flow.
(function () {
  const ACS = window.ACS;
  const { esc } = ACS.ui;
  const icon = ACS.icons.icon;
  const R = ACS.recon;

  // dev = existing DeviceView (edit) or null (new). Passwords are never echoed: the field starts empty.
  function formHtml(dev, o) {
    o = o || {};
    const d = dev || { name: '', host: '', username: 'admin', doorNo: 1, isCaptureDefault: false, isFingerReader: true };
    const hint = dev
      ? 'Để trống để giữ mật khẩu đã lưu. Chỉ nhập khi muốn đổi mật khẩu.'
      : 'Mật khẩu được mã hoá và lưu trên máy tính này, không hiện lại.';
    return '<form class="card devform" data-form novalidate>' +
      '<h3 class="h2">' + (dev ? 'Sửa ' + esc(dev.name) : (o.title || 'Thêm máy chấm công')) + '</h3>' +
      '<div class="formgrid">' +
      '<div><label class="field-lbl" for="df-name">Tên máy <span class="req">*</span></label>' +
      '<input id="df-name" data-f="name" class="input in48" type="text" autocomplete="off" placeholder="Ví dụ: Máy tầng trệt" value="' + esc(d.name) + '"></div>' +
      '<div><label class="field-lbl" for="df-host">Địa chỉ máy <span class="req">*</span></label>' +
      '<input id="df-host" data-f="host" class="input in48 in-mono" type="text" autocomplete="off" inputmode="decimal" placeholder="192.168.1.201" value="' + esc(d.host) + '"></div>' +
      '<div><label class="field-lbl" for="df-user">Tài khoản <span class="req">*</span></label>' +
      '<input id="df-user" data-f="username" class="input in48" type="text" autocomplete="off" value="' + esc(d.username) + '"></div>' +
      '<div><label class="field-lbl" for="df-pass">Mật khẩu' + (dev ? '' : ' <span class="req">*</span>') + '</label>' +
      '<input id="df-pass" data-f="password" class="input in48" type="password" autocomplete="new-password" placeholder="' + (dev ? 'Giữ nguyên mật khẩu đã lưu' : '') + '">' +
      '<div class="hint">' + hint + '</div></div>' +
      '<div><label class="field-lbl" for="df-door">Số cửa</label>' +
      '<input id="df-door" data-f="doorNo" class="input in48" type="number" min="1" max="8" value="' + esc(d.doorNo) + '"></div></div>' +
      '<label class="check"><input type="checkbox" data-f="isCaptureDefault"' + (d.isCaptureDefault ? ' checked' : '') + '><span>Dùng máy này để chụp mặt (máy chụp mặc định)</span></label>' +
      '<label class="check"><input type="checkbox" data-f="isFingerReader"' + (d.isFingerReader ? ' checked' : '') + '><span>Dùng máy này để quét vân tay</span></label>' +
      '<div data-err></div>' +
      '<div class="formbtns"><button type="button" class="btn" data-act="form-cancel">Huỷ</button>' +
      '<button type="submit" class="btn btn-primary" data-act="form-save">Lưu máy</button></div></form>';
  }

  // -> devices.save args (or {problem}).
  function readForm(form, id) {
    const v = {};
    form.querySelectorAll('[data-f]').forEach((el) => { v[el.dataset.f] = el.type === 'checkbox' ? el.checked : el.value; });
    const args = R.deviceArgs(v, id);
    return { args, problem: R.deviceProblem(args, id == null) };
  }

  function testHtml(t) {
    if (!t) return '';
    if (t.busy) return '<div class="alert alert-info" role="status">' + icon('spinner', 18).replace('class="ic"', 'class="ic xoay"') + '<span>Đang kiểm tra kết nối…</span></div>';
    if (t.error) return '<div class="alert alert-err" role="alert">' + icon('alertCircle', 18) + '<span>' + esc(t.error) + '</span></div>';
    const r = t.result;
    if (!r.ok) return '<div class="alert alert-err" role="alert">' + icon('alertCircle', 18) + '<span>' + esc(r.message) + '</span></div>';
    const yn = (ok) => (ok ? 'được' : 'chưa được');
    return '<div class="notice-ok" role="status">' + icon('checkCircle', 18) + '<div>Kết nối tốt · ' + esc(r.model || 'không rõ model') + (r.firmware ? ' · bản ' + esc(r.firmware) : '') +
      ' · ' + r.users + ' người trên máy<div class="small">Chụp mặt qua mạng: ' + yn(r.canCaptureFace) + ' · Quét vân tay qua mạng: ' + yn(r.canCaptureFinger) + '</div></div></div>';
  }

  ACS.devui = { formHtml, readForm, testHtml };
})();
