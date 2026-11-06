// Shared pieces of the four wizard screens: stepper, person header, per-device result rows, device picker.
(function () {
  const ACS = window.ACS;
  const { esc } = ACS.ui;
  const icon = ACS.icons.icon;
  const F = ACS.fmt;
  const NAMES = ['Thông tin', 'Khuôn mặt', 'Vân tay', 'Hoàn tất'];

  // active: 0..3 (4 = everything done). subs: { index: text } overrides the small caption under a step.
  function stepper(active, subs, names) {
    subs = subs || {};
    const NAMES_ = names || NAMES;
    return '<ol class="steps" aria-label="Các bước">' + NAMES_.map((n, i) => {
      if (i < active) {
        return '<li class="step step-done"><span class="step-n step-n-done">' + icon('check', 16, 2.4) + '</span><span><span class="step-t">' + n +
          '</span><span class="step-s">' + esc(subs[i] || 'Xong') + '</span></span></li>';
      }
      if (i === active) {
        return '<li class="step step-on" aria-current="step"><span class="step-n step-n-on">' + (i + 1) + '</span><span><span class="step-t">' + n +
          '</span><span class="step-s">' + esc(subs[i] || 'Đang làm') + '</span></span></li>';
      }
      return '<li class="step"><span class="step-n">' + (i + 1) + '</span><span><span class="step-t step-t-off">' + n +
        '</span><span class="step-s">' + esc(subs[i] || 'Tiếp theo') + '</span></span></li>';
    }).join('') + '</ol>';
  }

  function personHead(p) {
    return '<div class="whead"><span class="avatar avatar-md">' + esc(F.initials(p.fullName)) + '</span><div><h1 class="h1 h1-md">' + esc(p.fullName) +
      '</h1><div class="small muted mono">Mã NV ' + esc(p.employeeNo) + '</div></div></div>';
  }

  const SS = {
    ok: { cls: 'ok', icon: 'checkCircle', text: 'Đã nhận' },
    pending: { cls: 'wait', icon: 'clock', text: 'Chờ — tự gửi khi máy có mạng' },
    error: { cls: 'err', icon: 'alertCircle', text: 'Có lỗi' },
  };

  // The item of a device that matches a label ("Khuôn mặt", "Vân tay Trỏ phải"); falls back to the device's own state.
  function itemState(dev, label) {
    const it = (dev.items || []).find((x) => x.label === label);
    return it ? it : { label, state: dev.state, message: null, detail: null };
  }

  // Per-device result list. label = item label to look at.
  function deviceRows(devices, label) {
    return '<div class="dres" role="status">' + devices.map((d, i) => {
      const it = itemState(d, label);
      const s = SS[it.state] || SS.pending;
      return '<div class="dres-row dres-' + s.cls + '"' + (i ? ' style="border-top:1px solid var(--line-row)"' : '') + '>' +
        '<span class="st st-' + s.cls + '">' + icon(s.icon, 20) + '</span><span class="fw6 dres-n">' + esc(d.deviceName) + '</span>' +
        '<span class="st-' + s.cls + ' fw5">' + s.text + '</span>' +
        (it.message ? '<div class="dres-m small">' + esc(it.message) + '</div>' : '') +
        (it.detail ? '<details class="det dres-m"><summary>Chi tiết</summary><pre>' + esc(it.detail) + '</pre></details>' : '') + '</div>';
    }).join('') + '</div>';
  }


  // <select> of devices; the preselected one is the capture default. Offline devices stay selectable but say so.
  function deviceSelect(id, devices, selected) {
    return '<select id="' + id + '" class="input in48">' + devices.map((d) => {
      const off = d.authPaused ? ' (sai mật khẩu)' : (!d.online ? ' (đang mất kết nối)' : '');
      return '<option value="' + d.id + '"' + (d.id === selected ? ' selected' : '') + '>' + esc(d.name) + off + '</option>';
    }).join('') + '</select>';
  }

  function defaultDevice(devices) {
    const d = devices.find((x) => x.isCaptureDefault && x.online) || devices.find((x) => x.online) || devices.find((x) => x.isCaptureDefault) || devices[0];
    return d ? d.id : null;
  }

  function currentDevices() { return (ACS.state && ACS.state.devices) || []; }

  function errBox(msg) {
    return msg ? '<div class="alert alert-err" role="alert">' + icon('alertCircle', 18) + '<span>' + esc(msg) + '</span></div>' : '';
  }

  ACS.wizard = { stepper, personHead, deviceRows, deviceSelect, defaultDevice, currentDevices, errBox, itemState };
})();
