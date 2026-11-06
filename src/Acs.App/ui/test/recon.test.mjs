import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const R = createRequire(import.meta.url)('../js/recon.js');

const plan = { devices: [
  { deviceId: 1, deviceName: 'Máy tầng trệt', items: [], strangers: [{ deviceId: 1, employeeNo: '2001', name: 'Mai Thị Lan', faceCount: 1, fingerCount: 0 }] },
  { deviceId: 2, deviceName: 'Máy tầng 1', items: [
    { employeeNo: '1136', fullName: 'Trần Thị Bình', kind: 'MissingPerson', fingers: [] },
    { employeeNo: '1135', fullName: 'Lê Hoàng Cường', kind: 'MissingFingers', fingers: [7, 2] },
    { employeeNo: '1134', fullName: 'Phạm Dũng', kind: 'MissingFingers', fingers: [1] }],
  strangers: [{ deviceId: 2, employeeNo: '3001', name: 'A', faceCount: 0, fingerCount: 1 }, { deviceId: 2, employeeNo: '3002', name: 'B', faceCount: 1, fingerCount: 1 }] },
] };

test('describeItem covers every DiffKind in plain words', () => {
  const s = (kind, fingers) => R.describeItem({ employeeNo: '1136', fullName: 'Trần Thị Bình', kind, fingers: fingers || [] });
  assert.equal(s('MissingPerson'), 'Trần Thị Bình (mã 1136) chưa có trên máy này. App sẽ thêm vào.');
  assert.match(s('InfoMismatch'), /sửa trên máy cho giống app/);
  assert.match(s('MissingFace'), /thiếu khuôn mặt/);
  assert.equal(s('MissingFingers', [7, 2]), 'Trần Thị Bình (mã 1136) thiếu vân tay trỏ trái, trỏ phải. App sẽ gửi lại.');
  assert.match(s('ExtraFingers', [3]), /giữa phải mà app chưa có\. App chỉ báo, không tự xoá\./);
  assert.match(s('LockedButEnabled'), /khoá lại trên máy \(không xoá người\)/);
  assert.match(s('missingFace'), /thiếu khuôn mặt/, 'camelCase accepted');
  assert.match(s('Whatever'), /chưa khớp/);
});

test('summarizeDevice', () => {
  assert.equal(R.summarizeDevice(plan.devices[1]), 'Thiếu 1 người, 2 người thiếu vân tay, 2 người lạ');
  assert.equal(R.summarizeDevice({ items: [], strangers: [] }), 'Đã khớp');
});

test('buildStrangers keys by deviceId:employeeNo and defaults to skip', () => {
  assert.deepEqual(R.buildStrangers(plan, {}), { '1:2001': 'skip', '2:3001': 'skip', '2:3002': 'skip' });
  assert.deepEqual(R.buildStrangers(plan, { '2:3001': 'import', '2:3002': 'remove', '1:2001': 'bogus', '9:9': 'remove' }),
    { '1:2001': 'skip', '2:3001': 'import', '2:3002': 'remove' });
  assert.deepEqual(R.buildStrangers(null, null), {});
});

test('removals and nothingToDo', () => {
  assert.deepEqual(R.removals(plan, { '2:3002': 'remove', '2:3001': 'remove', '1:2001': 'import' }), [{ deviceName: 'Máy tầng 1', n: 2 }]);
  assert.deepEqual(R.removals(plan, {}), []);
  const only = { devices: [{ deviceId: 1, deviceName: 'x', items: [], strangers: [{ deviceId: 1, employeeNo: '5', name: 'n', faceCount: 0, fingerCount: 0 }] }] };
  assert.equal(R.nothingToDo(only, {}), true);
  assert.equal(R.nothingToDo(only, { '1:5': 'import' }), false);
  assert.equal(R.nothingToDo(plan, {}), false);
  assert.deepEqual(R.planTotals(plan), { items: 3, strangers: 3 });
});

test('backupPasswordProblem', () => {
  assert.match(R.backupPasswordProblem('short', 'short'), /ít nhất 8/);
  assert.match(R.backupPasswordProblem('longenough', 'different1'), /chưa giống/);
  assert.equal(R.backupPasswordProblem('longenough', 'longenough'), '');
});

test('fmtSize / fmtDateTime', () => {
  assert.equal(R.fmtSize(900), '900 B');
  assert.equal(R.fmtSize(1843200), '1,8 MB');
  assert.equal(R.fmtSize(20480), '20 KB');
  const now = new Date(2026, 10, 6, 12, 0).getTime();
  assert.equal(R.fmtDateTime(new Date(2026, 10, 6, 9, 5).toISOString(), now), '06/11 09:05');
  assert.equal(R.fmtDateTime(new Date(2025, 11, 31, 23, 59).toISOString(), now), '31/12/2025 23:59');
  assert.equal(R.fmtDateTime('bad'), '');
});

test('filterLog', () => {
  const rows = [
    { at: 'a', deviceName: 'Máy tầng 1', employeeNo: '1136', action: 'Gửi vân tay', ok: false, message: 'Không kết nối được' },
    { at: 'b', deviceName: 'Máy tầng trệt', employeeNo: '1137', action: 'Gửi khuôn mặt', ok: true, message: '' },
  ];
  assert.equal(R.filterLog(rows, { onlyErrors: true }).length, 1);
  assert.equal(R.filterLog(rows, { q: 'khuon mat' }).length, 1);
  assert.equal(R.filterLog(rows, { q: 'tầng 1' })[0].employeeNo, '1136');
  assert.equal(R.filterLog(rows, {}).length, 2);
});

test('deviceArgs / deviceProblem', () => {
  const a = R.deviceArgs({ name: ' Máy A ', host: ' 192.168.1.201 ', username: 'admin', password: '', doorNo: '2', isCaptureDefault: true }, 5);
  assert.deepEqual(a, { name: 'Máy A', host: '192.168.1.201', username: 'admin', password: '', doorNo: 2, isCaptureDefault: true, isFingerReader: false, id: 5 });
  assert.equal(R.deviceProblem(a, false), '', 'empty password allowed when editing');
  assert.match(R.deviceProblem(a, true), /mật khẩu/);
  assert.match(R.deviceProblem({ ...a, host: 'http://x' }, false), /không có "http/);
  assert.match(R.deviceProblem({ ...a, name: '' }, false), /đặt tên/);
  assert.equal(R.deviceArgs({ name: 'a', host: 'h', username: 'u', doorNo: 'x' }).doorNo, 1);
});
