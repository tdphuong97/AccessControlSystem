// A small stand-in for Cloudflare D1 over node:sqlite, enough for the Worker's own queries.
import { DatabaseSync } from 'node:sqlite';
import { readFileSync } from 'node:fs';

class Stmt {
  constructor(db, sql, args = []) { this.db = db; this.sql = sql; this.args = args; }
  bind(...args) { return new Stmt(this.db, this.sql, args); }
  async first(col) {
    const row = this.db.prepare(this.sql).get(...this.args);
    if (row === undefined) return null;
    return col ? row[col] : { ...row };
  }
  async all() { return { results: this.db.prepare(this.sql).all(...this.args).map((r) => ({ ...r })), success: true }; }
  async run() {
    const s = this.db.prepare(this.sql);
    if (/\breturning\b/i.test(this.sql)) {
      const results = s.all(...this.args).map((r) => ({ ...r }));
      return { results, success: true, meta: { changes: results.length } };
    }
    const r = s.run(...this.args);
    return { success: true, meta: { changes: Number(r.changes) } };
  }
}

export function d1(schemaPath) {
  const db = new DatabaseSync(':memory:');
  db.exec(readFileSync(schemaPath, 'utf8'));
  return {
    raw: db,
    prepare: (sql) => new Stmt(db, sql),
    async batch(stmts) {
      db.exec('BEGIN');
      try { const out = []; for (const s of stmts) out.push(await s.run()); db.exec('COMMIT'); return out; }
      catch (e) { db.exec('ROLLBACK'); throw e; }
    },
  };
}
