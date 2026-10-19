// Draws the monochrome macOS menu bar icon (template image) as PNG: a speaker outline with
// tweeter and woofer. Output: assets/trayTemplate.png (16 px) and assets/trayTemplate@2x.png (32 px).
import { mkdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { deflateSync } from 'node:zlib';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');

function insideRoundedRect(x, y, left, top, right, bottom, radius) {
  if (x < left || x > right || y < top || y > bottom) {
    return false;
  }

  const cx = Math.min(Math.max(x, left + radius), right - radius);
  const cy = Math.min(Math.max(y, top + radius), bottom - radius);
  return (x - cx) ** 2 + (y - cy) ** 2 <= radius ** 2;
}

// Shape in a 16 × 16 design grid.
function coverage(x, y) {
  const stroke = 1.5;
  const outer = insideRoundedRect(x, y, 2.5, 0.75, 13.5, 15.25, 2.75);
  const inner = insideRoundedRect(x, y, 2.5 + stroke, 0.75 + stroke, 13.5 - stroke, 15.25 - stroke, 2.75 - stroke);
  const tweeter = (x - 8) ** 2 + (y - 4.6) ** 2 <= 1.25 ** 2;
  const wooferDistance = Math.hypot(x - 8, y - 10.1);
  const woofer = wooferDistance <= 3.1 && wooferDistance >= 1.7;
  const cap = wooferDistance <= 0.75;
  return (outer && !inner) || tweeter || woofer || cap;
}

function render(size) {
  const samples = 8;
  const scale = 16 / size;
  const pixels = Buffer.alloc(size * size * 4);
  for (let py = 0; py < size; py++) {
    for (let px = 0; px < size; px++) {
      let hits = 0;
      for (let sy = 0; sy < samples; sy++) {
        for (let sx = 0; sx < samples; sx++) {
          if (coverage((px + (sx + 0.5) / samples) * scale, (py + (sy + 0.5) / samples) * scale)) {
            hits++;
          }
        }
      }

      const offset = (py * size + px) * 4;
      pixels[offset + 3] = Math.round((hits / (samples * samples)) * 255);
    }
  }

  return pixels;
}

const CRC_TABLE = Array.from({ length: 256 }, (_, n) => {
  let c = n;
  for (let k = 0; k < 8; k++) {
    c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
  }

  return c >>> 0;
});

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) {
    crc = CRC_TABLE[(crc ^ byte) & 0xff] ^ (crc >>> 8);
  }

  return (crc ^ 0xffffffff) >>> 0;
}

function chunk(type, data) {
  const length = Buffer.alloc(4);
  length.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([length, body, crc]);
}

function encodePng(size, rgba) {
  const header = Buffer.alloc(13);
  header.writeUInt32BE(size, 0);
  header.writeUInt32BE(size, 4);
  header[8] = 8; // bit depth
  header[9] = 6; // RGBA
  const rows = Buffer.alloc(size * (size * 4 + 1));
  for (let y = 0; y < size; y++) {
    rgba.copy(rows, y * (size * 4 + 1) + 1, y * size * 4, (y + 1) * size * 4);
  }

  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', header),
    chunk('IDAT', deflateSync(rows)),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

mkdirSync(join(root, 'assets'), { recursive: true });
writeFileSync(join(root, 'assets', 'trayTemplate.png'), encodePng(16, render(16)));
writeFileSync(join(root, 'assets', 'trayTemplate@2x.png'), encodePng(32, render(32)));
console.log('assets/trayTemplate.png and trayTemplate@2x.png written');
