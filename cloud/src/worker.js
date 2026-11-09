// acs-sync: shared data for Access Control System. The PCs encrypt every row before sending it; this Worker only
// orders the writes (a global version number), refuses stale ones, and hands out the "sender" lease.
const LEASE_TTL_MS = 90_000;
const MAX_PULL = 500;

const json = (body, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json; charset=utf-8' } });

function authorized(req, env) {
  const got = req.headers.get('authorization') || '';
  const want = 'Bearer ' + (env.CONNECT_KEY || '');
  if (!env.CONNECT_KEY || got.length !== want.length) return false;
  let diff = 0;
  for (let i = 0; i < want.length; i++) diff |= got.charCodeAt(i) ^ want.charCodeAt(i);
  return diff === 0;
}

async function nextVer(db) {
  const r = await db.prepare('UPDATE seq SET v = v + 1 WHERE id = 1 RETURNING v').run();
  return r.results[0].v;
}

const rowOut = (r) => r && ({ tbl: r.tbl, rid: r.rid, ver: r.ver, deleted: !!r.deleted, data: r.data ?? null, pc: r.pc ?? null, at: r.at });

async function getSpace(db) {
  const s = await db.prepare('SELECT wrapped, salt, iterations, created, ver FROM space WHERE id = 1').first();
  return s ? json(s) : json({ error: 'no space' }, 404);
}

async function putSpace(db, body, now) {
  const { wrapped, salt, iterations, baseVer } = body || {};
  if (!wrapped || !salt || !iterations) return json({ error: 'bad request' }, 400);
  const cur = await db.prepare('SELECT ver FROM space WHERE id = 1').first();
  if (!cur) {
    if (baseVer !== 0) return json({ ver: 0 }, 409);
    const r = await db.prepare('INSERT OR IGNORE INTO space(id, wrapped, salt, iterations, created, ver) VALUES(1, ?, ?, ?, ?, 1)')
      .bind(wrapped, salt, iterations, new Date(now).toISOString()).run();
    return r.meta.changes === 1 ? json({ ver: 1 }) : json({ ver: 1 }, 409);
  }
  if (baseVer !== cur.ver) return json({ ver: cur.ver }, 409);
  const r = await db.prepare('UPDATE space SET wrapped = ?, salt = ?, iterations = ?, ver = ver + 1 WHERE id = 1 AND ver = ?')
    .bind(wrapped, salt, iterations, cur.ver).run();
  return r.meta.changes === 1 ? json({ ver: cur.ver + 1 }) : json({ ver: cur.ver }, 409);
}

async function push(db, body, now) {
  const changes = Array.isArray(body?.changes) ? body.changes : null;
  if (!changes) return json({ error: 'bad request' }, 400);
  const at = new Date(now).toISOString();
  const results = [];
  for (const c of changes) {
    const { tbl, rid } = c;
    const base = Number(c.base) || 0;
    const deleted = c.deleted ? 1 : 0;
    const data = c.data ?? null;   // tombstones keep their sealed payload (natural key, who, when)
    const current = () => db.prepare('SELECT * FROM rows WHERE tbl = ? AND rid = ?').bind(tbl, rid).first();
    const cur = await current();
    let ok = false;
    let ver = 0;
    if (!cur && base === 0) {
      ver = await nextVer(db);
      const r = await db.prepare('INSERT INTO rows(tbl, rid, ver, deleted, data, pc, at) VALUES(?, ?, ?, ?, ?, ?, ?) ON CONFLICT DO NOTHING')
        .bind(tbl, rid, ver, deleted, data, c.pc ?? null, at).run();
      ok = r.meta.changes === 1;
    } else if (cur && base === cur.ver) {
      ver = await nextVer(db);
      const r = await db.prepare('UPDATE rows SET ver = ?, deleted = ?, data = ?, pc = ?, at = ? WHERE tbl = ? AND rid = ? AND ver = ?')
        .bind(ver, deleted, data, c.pc ?? null, at, tbl, rid, base).run();
      ok = r.meta.changes === 1;
    }
    results.push(ok ? { tbl, rid, ok: true, ver } : { tbl, rid, ok: false, current: rowOut(await current()) });
  }
  return json({ results });
}

async function pull(db, url) {
  const since = Math.max(0, Number(url.searchParams.get('since')) || 0);
  const limit = Math.min(MAX_PULL, Math.max(1, Number(url.searchParams.get('limit')) || 200));
  const { results } = await db.prepare('SELECT * FROM rows WHERE ver > ? ORDER BY ver LIMIT ?').bind(since, limit + 1).all();
  const more = results.length > limit;
  const rows = results.slice(0, limit).map(rowOut);
  return json({ rows, next: rows.length ? rows[rows.length - 1].ver : since, more });
}

async function lease(db, body, now) {
  const holder = String(body?.holder || '');
  if (!holder) return json({ error: 'bad request' }, 400);
  const active = body.active ? 1 : 0;
  const cur = await db.prepare("SELECT holder, active, label, until FROM lease WHERE name = 'sender'").first();
  const free = !cur || cur.until <= now || cur.holder === holder || (active && !cur.active);
  if (free) {
    const until = now + LEASE_TTL_MS;
    await db.prepare("INSERT INTO lease(name, holder, active, label, until) VALUES('sender', ?, ?, ?, ?) " +
      'ON CONFLICT(name) DO UPDATE SET holder = excluded.holder, active = excluded.active, label = excluded.label, until = excluded.until')
      .bind(holder, active, body.label ?? null, until).run();
    return json({ granted: true, holder, active: !!active, label: body.label ?? null, until });
  }
  return json({ granted: false, holder: cur.holder, active: !!cur.active, label: cur.label ?? null, until: cur.until });
}

async function release(db, body) {
  const r = await db.prepare("DELETE FROM lease WHERE name = 'sender' AND holder = ?").bind(String(body?.holder || '')).run();
  return json({ released: r.meta.changes === 1 });
}

export default {
  async fetch(req, env, ctx, now = Date.now()) {
    const url = new URL(req.url);
    const route = req.method + ' ' + url.pathname;
    if (route === 'GET /health') return json({ ok: true });
    if (!authorized(req, env)) return json({ error: 'unauthorized' }, 401);
    const body = req.method === 'GET' ? null : await req.json().catch(() => null);
    switch (route) {
      case 'GET /space': return getSpace(env.DB);
      case 'PUT /space': return putSpace(env.DB, body, now);
      case 'POST /push': return push(env.DB, body, now);
      case 'GET /pull': return pull(env.DB, url);
      case 'POST /lease': return lease(env.DB, body, now);
      case 'DELETE /lease': return release(env.DB, body);
      default: return json({ error: 'not found' }, 404);
    }
  },
};
