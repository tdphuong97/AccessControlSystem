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
