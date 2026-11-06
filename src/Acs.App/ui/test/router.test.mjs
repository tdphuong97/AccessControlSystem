import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const R = createRequire(import.meta.url)('../js/router.js');

test('parse routes', () => {
  assert.deepEqual(R.parse('#/people'), { name: 'people', params: {} });
  assert.deepEqual(R.parse('#/person/1137'), { name: 'person', params: { emp: '1137' } });
  assert.deepEqual(R.parse('#/new/finger/1137'), { name: 'new/finger', params: { emp: '1137' } });
  assert.deepEqual(R.parse('#/new/info'), { name: 'new/info', params: {} });
  for (const n of ['settings', 'reconcile', 'setup', 'log']) assert.equal(R.parse('#/' + n).name, n);
});
test('parse falls back to people', () => {
  assert.equal(R.parse('').name, 'people');
  assert.equal(R.parse('#/nonsense').name, 'people');
  assert.equal(R.parse('#/person').name, 'people');
  assert.equal(R.parse('#/new/zzz').name, 'people');
});
test('employeeNo stays a string', () => assert.equal(typeof R.parse('#/person/0012').params.emp, 'string'));
test('href round trip', () => assert.equal(R.href('person', '1137'), '#/person/1137'));
