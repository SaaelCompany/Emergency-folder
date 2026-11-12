const zlib = require('zlib');

function sdRoundBox(px, py, left, top, right, bottom, radius) {
  const cx = (left + right) / 2;
  const cy = (top + bottom) / 2;
  const hx = (right - left) / 2 - radius;
  const hy = (bottom - top) / 2 - radius;
  const dx = Math.abs(px - cx) - hx;
  const dy = Math.abs(py - cy) - hy;
  return Math.min(Math.max(dx, dy), 0) + Math.hypot(Math.max(dx, 0), Math.max(dy, 0)) - radius;
}

function cover(dist) {
  return Math.max(0, Math.min(1, 0.5 - dist / 0.01));
}

// Цвет папки в нормализованных координатах (0..1, y сверху вниз), RGBA 0..1.
function folderSample(x, y) {
  const tab = sdRoundBox(x, y, 0.14, 0.16, 0.52, 0.40, 0.055);
  const body = sdRoundBox(x, y, 0.10, 0.30, 0.90, 0.86, 0.075);
  const front = sdRoundBox(x, y, 0.10, 0.44, 0.90, 0.86, 0.075);
  const shape = Math.min(tab, body);
  const highlight = sdRoundBox(x, y, 0.18, 0.50, 0.82, 0.60, 0.04);

  let r = 0;
  let g = 0;
  let b = 0;
  let a = 0;
  function over(cr, cg, cb, ca) {
    const outA = ca + a * (1 - ca);
    if (outA <= 0) {
      a = 0;
      return;
    }
    r = (cr * ca + r * a * (1 - ca)) / outA;
    g = (cg * ca + g * a * (1 - ca)) / outA;
    b = (cb * ca + b * a * (1 - ca)) / outA;
    a = outA;
  }

  over(0.42, 0.26, 0.06, cover(shape - 0.012) * 0.9);
  over(0.45, 0.28, 0.08, cover(shape + 0.02) * 0.22);
  over(0.86, 0.55, 0.10, cover(tab));
  over(0.95, 0.70, 0.16, cover(body));
  over(1.0, 0.82, 0.30, cover(front));
  over(1.0, 0.94, 0.66, cover(highlight) * cover(front) * 0.7);
  if (cover(front) > 0 && y > 0.70) {
    over(0.72, 0.46, 0.08, Math.min(1, (y - 0.70) / 0.16) * 0.22 * cover(front));
  }
  return [r, g, b, a];
}

function drawFolderRGBA(size) {
  const scale = size >= 128 ? 2 : 4;
  const big = size * scale;
  const acc = new Float64Array(size * size * 4);
  for (let y = 0; y < big; y++) {
    for (let x = 0; x < big; x++) {
      const sample = folderSample((x + 0.5) / big, (y + 0.5) / big);
      const dx = Math.floor(x / scale);
      const dy = Math.floor(y / scale);
      const i = (dy * size + dx) * 4;
      const alpha = sample[3];
      acc[i] += sample[0] * alpha;
      acc[i + 1] += sample[1] * alpha;
      acc[i + 2] += sample[2] * alpha;
      acc[i + 3] += alpha;
    }
  }
  const out = Buffer.alloc(size * size * 4);
  const count = scale * scale;
  for (let i = 0; i < size * size; i++) {
    const alpha = acc[i * 4 + 3] / count;
    if (alpha <= 0) continue;
    const o = i * 4;
    out[o] = clampByte(acc[i * 4] / count / alpha * 255);
    out[o + 1] = clampByte(acc[i * 4 + 1] / count / alpha * 255);
    out[o + 2] = clampByte(acc[i * 4 + 2] / count / alpha * 255);
    out[o + 3] = clampByte(alpha * 255);
  }
  return out;
}

function clampByte(value) {
  return Math.max(0, Math.min(255, Math.round(value)));
}

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = (c & 1) ? (0xEDB88320 ^ (c >>> 1)) : (c >>> 1);
    table[n] = c >>> 0;
  }
  return table;
})();

function crc32(buf) {
  let crc = 0xFFFFFFFF;
  for (let i = 0; i < buf.length; i++) crc = CRC_TABLE[(crc ^ buf[i]) & 0xFF] ^ (crc >>> 8);
  return (crc ^ 0xFFFFFFFF) >>> 0;
}

function pngChunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const typeBuf = Buffer.from(type, 'ascii');
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(Buffer.concat([typeBuf, data])));
  return Buffer.concat([length, typeBuf, data, crc]);
}

function makeFolderPNG(size) {
  const rgba = drawFolderRGBA(size);
  const rowSize = 1 + size * 4;
  const raw = Buffer.alloc(rowSize * size);
  for (let y = 0; y < size; y++) {
    raw[y * rowSize] = 0;
    rgba.copy(raw, y * rowSize + 1, y * size * 4, (y + 1) * size * 4);
  }
  const signature = Buffer.from([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(size, 0);
  ihdr.writeUInt32BE(size, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([
    signature,
    pngChunk('IHDR', ihdr),
    pngChunk('IDAT', zlib.deflateSync(raw)),
    pngChunk('IEND', Buffer.alloc(0))
  ]);
}

function encodeBmpIcon(size, rgba) {
  const header = Buffer.alloc(40);
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(size, 4);
  header.writeInt32LE(size * 2, 8);
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);

  const xor = Buffer.alloc(size * size * 4);
  const maskStride = Math.ceil(size / 32) * 4;
  const and = Buffer.alloc(maskStride * size);
  for (let y = 0; y < size; y++) {
    const srcY = size - 1 - y;
    for (let x = 0; x < size; x++) {
      const src = (srcY * size + x) * 4;
      const dst = (y * size + x) * 4;
      const alpha = rgba[src + 3];
      const factor = alpha / 255;
      xor[dst] = clampByte(rgba[src + 2] * factor);
      xor[dst + 1] = clampByte(rgba[src + 1] * factor);
      xor[dst + 2] = clampByte(rgba[src] * factor);
      xor[dst + 3] = alpha;
      if (alpha < 128) {
        and[y * maskStride + (x >> 3)] |= 0x80 >> (x & 7);
      }
    }
  }
  return Buffer.concat([header, xor, and]);
}

function makeFolderICO(sizes) {
  const images = sizes.map(size => encodeBmpIcon(size, drawFolderRGBA(size)));
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(sizes.length, 4);
  const entries = Buffer.alloc(16 * sizes.length);
  let offset = 6 + entries.length;
  sizes.forEach((size, index) => {
    const entry = index * 16;
    entries[entry] = size >= 256 ? 0 : size;
    entries[entry + 1] = size >= 256 ? 0 : size;
    entries.writeUInt16LE(1, entry + 4);
    entries.writeUInt16LE(32, entry + 6);
    entries.writeUInt32LE(images[index].length, entry + 8);
    entries.writeUInt32LE(offset, entry + 12);
    offset += images[index].length;
  });
  return Buffer.concat([header, entries, ...images]);
}

module.exports = {
  drawFolderRGBA,
  makeFolderPNG,
  makeFolderICO
};
