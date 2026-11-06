import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const { fitWithin, dataUrlBytes, MAX_BYTES, MAX_SIDE } = createRequire(import.meta.url)('../js/image.js');

test('fitWithin scales the longest side down to max', () => {
  assert.deepEqual(fitWithin(1920, 1080, 640), { w: 640, h: 360 });
  assert.deepEqual(fitWithin(1080, 1920, 640), { w: 360, h: 640 });
});
test('fitWithin leaves small images unchanged', () => {
  assert.deepEqual(fitWithin(300, 400, 640), { w: 300, h: 400 });
  assert.deepEqual(fitWithin(640, 480, 640), { w: 640, h: 480 });
});
test('fitWithin never returns a zero side', () => {
  assert.deepEqual(fitWithin(10000, 1, 640), { w: 640, h: 1 });
});
test('dataUrlBytes counts the decoded bytes', () => {
  assert.equal(dataUrlBytes('data:image/jpeg;base64,QUJD'), 3);
  assert.equal(dataUrlBytes('data:image/jpeg;base64,QUI='), 2);
  assert.equal(dataUrlBytes('data:image/jpeg;base64,QQ=='), 1);
});
test('limits match the contract', () => {
  assert.equal(MAX_BYTES, 200 * 1024);
  assert.equal(MAX_SIDE, 640);
});
