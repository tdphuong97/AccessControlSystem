// Bridge to the C# host (C3). Inside WebView2 it posts {id, cmd, args} and resolves on {id, result|error};
// pushed {event, data} messages go to listeners. Opened in a plain browser it falls back to mock.js.
(function () {
  const ACS = (window.ACS = window.ACS || {});
  const webview = window.chrome && window.chrome.webview;
  const listeners = {};
  const pending = new Map();
  let nextId = 1;

  function emit(event, data) {
    (listeners[event] || []).slice().forEach((fn) => {
      try { fn(data); } catch (e) { console.error(e); }
    });
  }

  function receive(msg) {
    if (typeof msg === 'string') msg = JSON.parse(msg);
    if (msg && msg.id && pending.has(msg.id)) {
      const p = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) p.reject(new Error(msg.error));
      else p.resolve(msg.result);
    } else if (msg && msg.event) {
      emit(msg.event, msg.data);
    }
  }

  const bridge = {
    real: !!webview,
    // Returns an unsubscribe function so screens can clean up when the route changes.
    on(event, fn) {
      (listeners[event] = listeners[event] || []).push(fn);
      return () => { listeners[event] = (listeners[event] || []).filter((f) => f !== fn); };
    },
    emit,
    call(cmd, args) {
      if (!bridge.real) return ACS.mock.call(cmd, args || {});
      const id = nextId++;
      return new Promise((resolve, reject) => {
        pending.set(id, { resolve, reject });
        webview.postMessage({ id, cmd, args: args || {} });
      });
    },
  };

  if (webview) webview.addEventListener('message', (e) => receive(e.data));
  ACS.bridge = bridge;
})();
