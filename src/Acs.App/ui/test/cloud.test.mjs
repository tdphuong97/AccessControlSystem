import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const C = createRequire(import.meta.url)('../js/cloud.js');

const on = { enabled: true, connected: true, online: true, lastSyncAt: '2026-11-09T13:40:05', pending: 0, isSender: true, senderName: 'NHIEN-PC' };

test('statusLine shows synced time', () => {
  assert.equal(C.statusLine(on), 'Đã đồng bộ lúc 13:40');
  assert.equal(C.statusLine({ ...on, pending: 2 }), 'Đã đồng bộ lúc 13:40 · còn 2 thay đổi chưa gửi');
  assert.equal(C.statusLine({ ...on, lastSyncAt: null }), 'Đang đồng bộ…');
});

test('statusLine shows offline with pending count', () => {
  assert.equal(C.statusLine({ ...on, online: false, pending: 3 }), 'Mất kết nối Internet, thay đổi đang giữ trên máy (3).');
  assert.equal(C.statusLine({ ...on, online: false, pending: 0 }), 'Mất kết nối Internet. Phần mềm vẫn chạy bằng dữ liệu trên máy.');
});

test('statusLine when off or never connected', () => {
  assert.equal(C.statusLine({ enabled: false, connected: true }), 'Dữ liệu chung đang tắt trên máy này. Máy dùng riêng dữ liệu của nó.');
  assert.equal(C.statusLine({ enabled: false, connected: false }), 'Chưa kết nối dữ liệu chung.');
});

test('senderLine names the PC that sends', () => {
  assert.equal(C.senderLine(on), 'Máy này đang gửi xuống máy chấm công.');
  assert.equal(C.senderLine({ ...on, isSender: false, senderName: 'HUONG-PC' }), 'Đang gửi xuống máy chấm công: HUONG-PC.');
  assert.equal(C.senderLine({ ...on, isSender: false, senderName: null }), '');
  assert.equal(C.senderLine({ enabled: false }), '');
});

test('codeLooksValid accepts acs1 codes only', () => {
  assert.equal(C.codeLooksValid(' acs1:acs-sync.vienba.workers.dev:Ab_c-12 '), true);
  assert.equal(C.codeLooksValid('acs1:host'), false);
  assert.equal(C.codeLooksValid('https://acs-sync.vienba.workers.dev'), false);
  assert.equal(C.codeLooksValid(''), false);
});

test('passwordProblem needs 8 chars and a matching repeat when creating', () => {
  assert.equal(C.passwordProblem('1234567', '1234567', true), 'Mật khẩu dữ liệu chung cần ít nhất 8 ký tự.');
  assert.equal(C.passwordProblem('12345678', '12345679', true), 'Hai lần nhập mật khẩu chưa giống nhau. Hãy nhập lại cho khớp.');
  assert.equal(C.passwordProblem('12345678', '12345678', true), '');
  assert.equal(C.passwordProblem('', undefined, false), 'Hãy nhập mật khẩu dữ liệu chung.');
  assert.equal(C.passwordProblem('x', undefined, false), '');
});
