// Screen lifecycle: every mount gets a brand-new host element, so listeners attached by an earlier screen can
// never fire again; the previous screen's cleanup runs and its ctx.alive() turns false (late async work is dropped).
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else { root.ACS = root.ACS || {}; root.ACS.mount = api; }
})(typeof self !== 'undefined' ? self : this, function () {
  // container: element-like with replaceChildren(); createEl(): returns a new empty host element.
  function createMounter(container, createEl) {
    let cleanup = null, ctx = null;
    function dispose() {
      if (ctx) ctx.disposed = true;
      if (cleanup) { try { cleanup(); } catch (e) { console.error(e); } }
      cleanup = null; ctx = null;
    }
    return {
      mount(render) {
        dispose();
        const host = createEl();
        const c = { disposed: false, alive() { return !c.disposed; } };
        ctx = c;
        container.replaceChildren(host);
        cleanup = render(host, c) || null;
        return host;
      },
      dispose,
    };
  }
  return { createMounter };
});
