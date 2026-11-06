// Face image normalising: scale the longest side to <= 640 px and lower JPEG quality until the file is <= 200 KB.
(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else { root.ACS = root.ACS || {}; root.ACS.image = api; }
})(typeof self !== 'undefined' ? self : this, function () {
  const MAX_SIDE = 640;
  const MAX_BYTES = 200 * 1024;

  // Pure size maths: shrink (w, h) so the longest side is at most max; never enlarge.
  function fitWithin(w, h, max) {
    const m = Math.max(w, h);
    if (!(m > max)) return { w, h };
    const k = max / m;
    return { w: Math.max(1, Math.round(w * k)), h: Math.max(1, Math.round(h * k)) };
  }

  // Decoded byte size of a base64 data URL.
  function dataUrlBytes(url) {
    const i = String(url).indexOf(',');
    const b64 = i >= 0 ? url.slice(i + 1) : url;
    const pad = b64.endsWith('==') ? 2 : (b64.endsWith('=') ? 1 : 0);
    return Math.floor(b64.length * 3 / 4) - pad;
  }

  // canvas, <img> or <video> -> Promise<JPEG data URL> that is <= 200 KB.
  async function normalizeFace(src) {
    const sw = src.naturalWidth || src.videoWidth || src.width;
    const sh = src.naturalHeight || src.videoHeight || src.height;
    if (!sw || !sh) throw new Error('Không đọc được ảnh này. Hãy chọn ảnh JPG hoặc PNG khác.');
    const { w, h } = fitWithin(sw, sh, MAX_SIDE);
    const c = document.createElement('canvas');
    c.width = w; c.height = h;
    const g = c.getContext('2d');
    g.fillStyle = '#FFFFFF';
    g.fillRect(0, 0, w, h);
    g.drawImage(src, 0, 0, w, h);
    for (let i = 9; i >= 5; i--) {
      const url = c.toDataURL('image/jpeg', i / 10);
      if (dataUrlBytes(url) <= MAX_BYTES) return url;
    }
    throw new Error('Ảnh quá lớn, hãy chọn ảnh khác');
  }

  return { MAX_SIDE, MAX_BYTES, fitWithin, dataUrlBytes, normalizeFace };
});
