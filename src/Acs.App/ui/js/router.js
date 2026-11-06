// Hash router. Routes: #/people, #/person/:emp, #/new/info|face|finger|done[/:emp], #/settings, #/reconcile, #/setup, #/log
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else { root.ACS = root.ACS || {}; root.ACS.router = api; }
})(typeof self !== 'undefined' ? self : this, function () {
  const SIMPLE = ['people', 'settings', 'reconcile', 'setup', 'log'];
  const STEPS = ['info', 'face', 'finger', 'done'];

  // "#/person/1137" -> { name: 'person', params: { emp: '1137' } }. Unknown paths go to the people list.
  function parse(hash) {
    let parts;
    try { parts = String(hash || '').replace(/^#\/?/, '').split('/').filter(Boolean).map(decodeURIComponent); }
    catch (e) { return { name: 'people', params: {} }; }
    const [a, b, c] = parts;
    if (a === 'person' && b) return { name: 'person', params: { emp: b } };
    if (a === 'new' && STEPS.includes(b)) return { name: 'new/' + b, params: c ? { emp: c } : {} };
    if (SIMPLE.includes(a)) return { name: a, params: {} };
    return { name: 'people', params: {} };
  }

  function href(name, emp) {
    if (name === 'person') return '#/person/' + encodeURIComponent(emp);
    return '#/' + name + (emp ? '/' + encodeURIComponent(emp) : '');
  }

  let onChange = null;
  function fire() { if (onChange) onChange(parse(location.hash)); }
  function start(fn) { onChange = fn; window.addEventListener('hashchange', fire); fire(); }
  function go(name, emp) { const h = href(name, emp); if (location.hash === h) fire(); else location.hash = h; }

  return { parse, href, start, go };
});
