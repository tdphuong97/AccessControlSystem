import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const F = createRequire(import.meta.url)('../js/format.js');

test('initials: first letter of first + last word', () => assert.equal(F.initials('Nguyễn Văn An'), 'NA'));
test('initials: first + last word, keeps diacritics', () => {
  assert.equal(F.initials('Hoàng Minh Đức'), 'HĐ');
  assert.equal(F.initials('Trần Thị Bình'), 'TB');
  assert.equal(F.initials('đặng quốc huy'), 'ĐH');
});
test('initials: one-word, empty and spacey names', () => {
  assert.equal(F.initials('Linh'), 'L');
  assert.equal(F.initials('  Võ   Hà  '), 'VH');
  assert.equal(F.initials(''), '');
  assert.equal(F.initials(null), '');
});

test('fingerLabel: ten C1 labels', () => {
  const want = ['Cái phải', 'Trỏ phải', 'Giữa phải', 'Áp út phải', 'Út phải',
    'Cái trái', 'Trỏ trái', 'Giữa trái', 'Áp út trái', 'Út trái'];
  for (let i = 1; i <= 10; i++) assert.equal(F.fingerLabel(i), want[i - 1]);
  assert.equal(F.fingerLabel(7), 'Trỏ trái');
});
test('fingerLabel: out of range', () => {
  assert.equal(F.fingerLabel(0), 'Ngón 0');
  assert.equal(F.fingerLabel(11), 'Ngón 11');
});

test('timeAgo', () => {
  const now = Date.parse('2026-11-06T10:00:00Z');
  const ago = (s) => new Date(now - s * 1000).toISOString();
  assert.equal(F.timeAgo(ago(20), now), 'vừa xong');
  assert.equal(F.timeAgo(ago(5 * 60), now), '5 phút trước');
  assert.equal(F.timeAgo(ago(3 * 3600), now), '3 giờ trước');
  assert.equal(F.timeAgo(ago(2 * 86400), now), '2 ngày trước');
  assert.equal(F.timeAgo(null, now), 'chưa rõ');
  assert.equal(F.timeAgo(ago(-60), now), 'vừa xong');
});
test('durationText for device badge', () => {
  const now = Date.parse('2026-11-06T10:00:00Z');
  assert.equal(F.durationText(5 * 60), '5 phút');
  assert.equal(F.durationText(90 * 60), '1 giờ');
  assert.equal(F.durationText(30), 'dưới 1 phút');
});

test('paginate: one page of a list, clamped to the pages that exist', () => {
  assert.deepEqual(F.paginate(157, 50, 1), { page: 1, pages: 4, from: 0, to: 50 });
  assert.deepEqual(F.paginate(157, 50, 4), { page: 4, pages: 4, from: 150, to: 157 });
  assert.deepEqual(F.paginate(157, 50, 9), { page: 4, pages: 4, from: 150, to: 157 }, 'past the end: the last page');
  assert.deepEqual(F.paginate(157, 50, 0), { page: 1, pages: 4, from: 0, to: 50 });
  assert.deepEqual(F.paginate(157, 0, 3), { page: 1, pages: 1, from: 0, to: 157 }, '0 = everyone on one page');
  assert.deepEqual(F.paginate(0, 50, 1), { page: 1, pages: 1, from: 0, to: 0 });
});

test('pageSize: a remembered choice from PAGE_SIZES, otherwise 50', () => {
  assert.deepEqual(F.PAGE_SIZES, [20, 50, 100, 0]);
  assert.equal(F.pageSize('20'), 20);
  assert.equal(F.pageSize('0'), 0);
  assert.equal(F.pageSize('37'), 50);
  assert.equal(F.pageSize(null), 50);
});

test('groupFromName: the code before the first dot', () => {
  assert.equal(F.groupFromName('ABC. Lan'), 'ABC');
  assert.equal(F.groupFromName('XYZ.KT.Minh'), 'XYZ');
  assert.equal(F.groupFromName('  deF . Minh'), 'DEF');
  for (const n of ['BAOVE', 'Tran Thi Mai', 'Nguyen Van A. B', 'TOOLONG. An', '. An', 'A-B. An', '', null]) assert.equal(F.groupFromName(n), null, String(n));
});

test('groupList: every group once with its head count, sorted by name', () => {
  const rows = [{ groupName: 'XYZ' }, { groupName: 'ABC' }, { groupName: 'XYZ' }, { groupName: null }, { groupName: 'Đông Á' }, { groupName: 'Bảo vệ' }];
  assert.deepEqual(F.groupList(rows), [{ name: 'ABC', count: 1 }, { name: 'Bảo vệ', count: 1 }, { name: 'Đông Á', count: 1 }, { name: 'XYZ', count: 2 }]);
  assert.deepEqual(F.groupList([]), []);
});

test('byGroup: all, one group, or the people without a group', () => {
  const rows = [{ n: 1, groupName: 'XYZ' }, { n: 2, groupName: null }, { n: 3, groupName: 'ABC' }, { n: 4 }];
  assert.deepEqual(F.byGroup(rows, '').map((r) => r.n), [1, 2, 3, 4]);
  assert.deepEqual(F.byGroup(rows, 'XYZ').map((r) => r.n), [1]);
  assert.deepEqual(F.byGroup(rows, F.NO_GROUP).map((r) => r.n), [2, 4]);
});

test('sortRows: by employee number as numbers, both ways; name keeps the server order', () => {
  const rows = [{ employeeNo: '100' }, { employeeNo: '2' }, { employeeNo: 'A7' }, { employeeNo: '10' }, { employeeNo: '0012' }, { employeeNo: '99999999999999999999' }];
  assert.deepEqual(F.sortRows(rows, 'noAsc').map((r) => r.employeeNo), ['2', '10', '0012', '100', '99999999999999999999', 'A7']);
  assert.deepEqual(F.sortRows(rows, 'noDesc').map((r) => r.employeeNo), ['A7', '99999999999999999999', '100', '0012', '10', '2']);
  assert.deepEqual(F.sortRows(rows, 'name'), rows);
  assert.notEqual(F.sortRows(rows, 'noAsc'), rows);   // never sorts the caller's array in place
});

test('sortMode: a saved choice or the name order', () => {
  assert.equal(F.sortMode('noAsc'), 'noAsc');
  assert.equal(F.sortMode('noDesc'), 'noDesc');
  for (const v of [null, '', 'bogus']) assert.equal(F.sortMode(v), 'name');
});
