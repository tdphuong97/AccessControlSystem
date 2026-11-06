// Inline SVG icons from the approved mockup (24x24 grid, stroke style via the .ic class in app.css).
(function () {
  const ACS = (window.ACS = window.ACS || {});
  const PATHS = {
    people: '<circle cx="9.5" cy="8" r="3.5"/><path d="M3 19.5v-1a4 4 0 0 1 4-4h5a4 4 0 0 1 4 4v1"/><path d="M16 4.6a3.5 3.5 0 0 1 0 6.8M18.5 14.7a4 4 0 0 1 2.5 3.8v1"/>',
    device: '<rect x="6" y="2.5" width="12" height="19" rx="2.5"/><circle cx="12" cy="8.5" r="2.5"/><path d="M9.5 15h5M10.5 18h3"/>',
    download: '<path d="M12 4v11M7.5 10.5L12 15l4.5-4.5M5 19.5h14"/>',
    addPerson: '<circle cx="10" cy="8" r="3.5"/><path d="M3.5 19.5v-1a4 4 0 0 1 4-4h5a4 4 0 0 1 4 4v1"/><path d="M19 8v6M16 11h6"/>',
    search: '<circle cx="11" cy="11" r="6.5"/><path d="M20 20l-4.2-4.2"/>',
    checkCircle: '<circle cx="12" cy="12" r="9"/><path d="M8 12.5l2.8 2.8 5.4-5.6"/>',
    alertCircle: '<circle cx="12" cy="12" r="9"/><path d="M12 7.5v5.5M12 16.5h.01"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    check: '<path d="M4.5 12.5l4.5 4.5L19.5 7"/>',
    chevronRight: '<path d="M9 6l6 6-6 6"/>',
    chevronLeft: '<path d="M15 6l-6 6 6 6"/>',
    refresh: '<path d="M20 11.5A8 8 0 0 0 5.6 7.2L4 9M4 4.5V9h4.5"/><path d="M4 12.5a8 8 0 0 0 14.4 4.3L20 15M20 19.5V15h-4.5"/>',
    camera: '<path d="M4 8.5A1.5 1.5 0 0 1 5.5 7h2l1.2-2h6.6L16.5 7h2A1.5 1.5 0 0 1 20 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-13A1.5 1.5 0 0 1 4 17.5z"/><circle cx="12" cy="12.8" r="3.3"/>',
    fingerprint: '<path d="M6.5 10.5a5.5 5.5 0 0 1 11 0v1.5M4.5 14c0-1.2.2-2.3.5-3.3M9 19.5c.9-1.4 1.5-3 1.5-5a1.5 1.5 0 0 1 3 0c0 3-.8 5-2.2 6.5M14.5 19c1-1.6 1.5-3.2 1.5-5v-2.5M12 6.5a3.5 3.5 0 0 0-3.5 3.5v1.5"/>',
    edit: '<path d="M4 20h4l10.5-10.5a2.1 2.1 0 0 0-3-3L5 17z"/><path d="M13.5 8.5l3 3"/>',
    lock: '<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V8a4 4 0 0 1 8 0v2.5"/>',
    unlock: '<rect x="5" y="10.5" width="14" height="10" rx="2"/><path d="M8 10.5V8a4 4 0 0 1 7.5-2"/>',
    trash: '<path d="M4.5 7h15M9.5 7V4.5h5V7M6.5 7l.8 12.5h9.4L17.5 7M10 11v5.5M14 11v5.5"/>',
    x: '<path d="M6 6l12 12M18 6L6 18"/>',
    plus: '<path d="M12 5v14M5 12h14"/>',
  };
  const LOGO = '<svg width="20" height="20" viewBox="0 0 24 24" aria-hidden="true" style="flex-shrink:0"><rect width="24" height="24" rx="6" fill="#1D5FD1"/><path d="M7 9.5V8a1 1 0 0 1 1-1h1.5M14.5 7H16a1 1 0 0 1 1 1v1.5M17 14.5V16a1 1 0 0 1-1 1h-1.5M9.5 17H8a1 1 0 0 1-1-1v-1.5" fill="none" stroke="#FFFFFF" stroke-width="1.6" stroke-linecap="round"/><circle cx="10.3" cy="10.8" r="0.9" fill="#FFFFFF"/><circle cx="13.7" cy="10.8" r="0.9" fill="#FFFFFF"/><path d="M10 13.6c1.1.9 2.9.9 4 0" fill="none" stroke="#FFFFFF" stroke-width="1.4" stroke-linecap="round"/></svg>';

  // icon('people', 18) -> '<svg class="ic" ...>'
  function icon(name, size, strokeWidth) {
    const s = size || 18;
    const sw = strokeWidth ? ' style="stroke-width:' + strokeWidth + '"' : '';
    return '<svg class="ic" width="' + s + '" height="' + s + '" viewBox="0 0 24 24" aria-hidden="true"' + sw + '>' + (PATHS[name] || '') + '</svg>';
  }
  ACS.icons = { PATHS, LOGO, icon };
})();
