import test from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
const { createMounter } = createRequire(import.meta.url)('../js/mount.js');

// Minimal DOM shim: elements record listeners; a click only reaches elements currently attached to the container.
function makeDom() {
  const container = { children: [], replaceChildren(...els) { this.children = els; } };
  const created = [];
  const createEl = () => { const el = { listeners: [], addEventListener(t, fn) { this.listeners.push(fn); } }; created.push(el); return el; };
  const click = () => container.children.forEach((el) => el.listeners.forEach((fn) => fn()));
  return { container, createEl, created, click };
}

test('two mounts leave exactly one live handler', () => {
  const d = makeDom();
  const m = createMounter(d.container, d.createEl);
  const fired = [];
  const screen = (name) => (host) => { host.addEventListener('click', () => fired.push(name)); };
  m.mount(screen('A'));
  m.mount(screen('B'));
  assert.equal(d.container.children.length, 1);
  assert.equal(d.container.children.reduce((n, el) => n + el.listeners.length, 0), 1);
  d.click();
  assert.deepEqual(fired, ['B']);
});

test('remount runs the previous cleanup once and flips alive()', () => {
  const d = makeDom();
  const m = createMounter(d.container, d.createEl);
  let cleaned = 0, ctxA;
  m.mount((host, ctx) => { ctxA = ctx; return () => { cleaned++; }; });
  assert.equal(ctxA.alive(), true);
  m.mount(() => null);
  assert.equal(cleaned, 1);
  assert.equal(ctxA.alive(), false);
  m.dispose();
  assert.equal(cleaned, 1);
});

test('late async result of a replaced screen is dropped', async () => {
  const d = makeDom();
  const m = createMounter(d.container, d.createEl);
  const drawn = [];
  const slow = (name, ms) => (host, ctx) => { new Promise((r) => setTimeout(r, ms)).then(() => { if (ctx.alive()) drawn.push(name); }); };
  m.mount(slow('A', 20));
  m.mount(slow('B', 1));
  await new Promise((r) => setTimeout(r, 40));
  assert.deepEqual(drawn, ['B']);
});
