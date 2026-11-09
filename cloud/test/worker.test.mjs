import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import { d1 } from './d1shim.mjs';
import worker from '../src/worker.js';

const schema = fileURLToPath(new URL('../schema.sql', import.meta.url));
const make = () => ({ DB: d1(schema), CONNECT_KEY: 'k' });

async function call(env, method, path, body, key = 'k', now) {
  const headers = { 'content-type': 'application/json' };
  if (key) headers.authorization = 'Bearer ' + key;
  const req = new Request('https://acs.test' + path, { method, headers, body: body === undefined ? undefined : JSON.stringify(body) });
  const res = await worker.fetch(req, env, undefined, now);
  const text = await res.text();
  return { status: res.status, json: text ? JSON.parse(text) : null };
}
const ch = (rid, base, data = 'x', deleted = false) => ({ tbl: 'people', rid, base, deleted, data: deleted ? null : data, pc: 'pc1' });

test('health needs no key', async () => {
  const r = await call(make(), 'GET', '/health', undefined, null);
  assert.equal(r.status, 200);
  assert.deepEqual(r.json, { ok: true });
});

test('everything else is 401 without the key', async () => {
  const env = make();
  for (const [m, p] of [['GET', '/space'], ['POST', '/push'], ['GET', '/pull?since=0'], ['POST', '/lease']]) {
    assert.equal((await call(env, m, p, m === 'GET' ? undefined : {}, null)).status, 401);
    assert.equal((await call(env, m, p, m === 'GET' ? undefined : {}, 'wrong')).status, 401);
  }
});

test('space: 404, create with baseVer 0, second create is 409, replace with matching baseVer bumps ver', async () => {
  const env = make();
  assert.equal((await call(env, 'GET', '/space')).status, 404);
  const s = { wrapped: 'w1', salt: 's1', iterations: 600000, baseVer: 0 };
  const c = await call(env, 'PUT', '/space', s);
  assert.equal(c.status, 200); assert.equal(c.json.ver, 1);
  assert.equal((await call(env, 'PUT', '/space', s)).status, 409);
  const g = await call(env, 'GET', '/space');
  assert.equal(g.json.wrapped, 'w1'); assert.equal(g.json.ver, 1); assert.ok(g.json.created);
  const r = await call(env, 'PUT', '/space', { wrapped: 'w2', salt: 's2', iterations: 600000, baseVer: 1 });
  assert.equal(r.json.ver, 2);
  assert.equal((await call(env, 'GET', '/space')).json.wrapped, 'w2');
});

test('push insert then update with right base; stale base returns ok:false with current row', async () => {
  const env = make();
  const a = await call(env, 'POST', '/push', { changes: [ch('r1', 0, 'v1')] });
  assert.equal(a.json.results[0].ok, true);
  const v1 = a.json.results[0].ver;
  const b = await call(env, 'POST', '/push', { changes: [ch('r1', v1, 'v2')] });
  assert.equal(b.json.results[0].ok, true);
  assert.ok(b.json.results[0].ver > v1);
  const c = await call(env, 'POST', '/push', { changes: [ch('r1', v1, 'v3')] });
  assert.equal(c.json.results[0].ok, false);
  assert.equal(c.json.results[0].current.data, 'v2');
  assert.equal(c.json.results[0].current.ver, b.json.results[0].ver);
});

test('push with base 0 on an existing row is refused', async () => {
  const env = make();
  await call(env, 'POST', '/push', { changes: [ch('r1', 0, 'v1')] });
  const r = await call(env, 'POST', '/push', { changes: [ch('r1', 0, 'other')] });
  assert.equal(r.json.results[0].ok, false);
  assert.equal(r.json.results[0].current.data, 'v1');
});

test('push tombstone (deleted:true, data:null) is pulled as deleted', async () => {
  const env = make();
  const a = await call(env, 'POST', '/push', { changes: [ch('r1', 0, 'v1')] });
  await call(env, 'POST', '/push', { changes: [ch('r1', a.json.results[0].ver, null, true)] });
  const p = await call(env, 'GET', '/pull?since=0');
  assert.equal(p.json.rows.length, 1);
  assert.equal(p.json.rows[0].deleted, true);
  assert.equal(p.json.rows[0].data, null);
});

test('pull pages: since/next/more, ordered by ver, limit capped at 500', async () => {
  const env = make();
  const many = Array.from({ length: 520 }, (_, i) => ch('r' + i, 0, 'd' + i));
  await call(env, 'POST', '/push', { changes: many });
  const p1 = await call(env, 'GET', '/pull?since=0&limit=9999');
  assert.equal(p1.json.rows.length, 500);
  assert.equal(p1.json.more, true);
  const vers = p1.json.rows.map((r) => r.ver);
  assert.deepEqual(vers, [...vers].sort((x, y) => x - y));
  const p2 = await call(env, 'GET', '/pull?since=' + p1.json.next + '&limit=100');
  assert.equal(p2.json.rows.length, 20);
  assert.equal(p2.json.more, false);
  const p3 = await call(env, 'GET', '/pull?since=0&limit=3');
  assert.equal(p3.json.rows.length, 3); assert.equal(p3.json.next, p3.json.rows[2].ver);
});

test('lease: first holder granted; other inactive holder refused; active takes over from inactive; expired is free; only holder releases', async () => {
  const env = make();
  const t0 = 1_000_000;
  let r = await call(env, 'POST', '/lease', { holder: 'A', active: false, label: 'la' }, 'k', t0);
  assert.equal(r.json.granted, true); assert.equal(r.json.holder, 'A');
  r = await call(env, 'POST', '/lease', { holder: 'B', active: false, label: 'lb' }, 'k', t0 + 1000);
  assert.equal(r.json.granted, false); assert.equal(r.json.holder, 'A'); assert.equal(r.json.label, 'la');
  r = await call(env, 'POST', '/lease', { holder: 'B', active: true, label: 'lb' }, 'k', t0 + 2000);
  assert.equal(r.json.granted, true); assert.equal(r.json.holder, 'B');
  r = await call(env, 'POST', '/lease', { holder: 'A', active: true, label: 'la' }, 'k', t0 + 3000);
  assert.equal(r.json.granted, false, 'an active holder is not taken over');
  r = await call(env, 'POST', '/lease', { holder: 'A', active: false, label: 'la' }, 'k', t0 + 2000 + 91_000);
  assert.equal(r.json.granted, true, 'expired after 90 s');
  r = await call(env, 'DELETE', '/lease', { holder: 'B' }, 'k', t0 + 100_000);
  assert.equal(r.json.released, false);
  r = await call(env, 'DELETE', '/lease', { holder: 'A' }, 'k', t0 + 100_000);
  assert.equal(r.json.released, true);
  r = await call(env, 'POST', '/lease', { holder: 'B', active: false, label: 'lb' }, 'k', t0 + 100_001);
  assert.equal(r.json.granted, true);
});
