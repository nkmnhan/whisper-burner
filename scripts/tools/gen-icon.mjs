// Generates AppIcon.png (256) + AppIcon.ico (16,24,32,48,64,128,256) from AppIcon.svg
import { readFileSync, writeFileSync } from 'fs';
import { fileURLToPath } from 'url';
import { dirname, join } from 'path';
import sharp from 'sharp';

const dir   = dirname(fileURLToPath(import.meta.url));
const svg   = readFileSync(join(dir, 'AppIcon.svg'));

const sizes = [16, 24, 32, 48, 64, 128, 256];

// Render all sizes to PNG buffers
const pngBuffers = await Promise.all(
  sizes.map(s => sharp(svg).resize(s, s).png().toBuffer())
);

// Write 256×256 PNG (used by the app at runtime)
writeFileSync(join(dir, 'AppIcon.png'), pngBuffers[pngBuffers.length - 1]);
console.log('✓ AppIcon.png (256×256)');

// Build ICO manually: ICO header + directory + PNG data chunks
// ICO format: ICONDIR (6 bytes) + N × ICONDIRENTRY (16 bytes each) + image data
const n = sizes.length;
const headerSize  = 6;
const dirEntrySize = 16;
const dirSize     = headerSize + n * dirEntrySize;

// Calculate offsets for each image
const offsets = [];
let offset = dirSize;
for (const buf of pngBuffers) { offsets.push(offset); offset += buf.length; }

const icoHeader = Buffer.alloc(6);
icoHeader.writeUInt16LE(0,    0); // reserved
icoHeader.writeUInt16LE(1,    2); // type: 1 = ICO
icoHeader.writeUInt16LE(n,    4); // image count

const dirEntries = pngBuffers.map((buf, i) => {
  const e = Buffer.alloc(16);
  const s = sizes[i];
  e.writeUInt8(s >= 256 ? 0 : s, 0);  // width  (0 = 256)
  e.writeUInt8(s >= 256 ? 0 : s, 1);  // height (0 = 256)
  e.writeUInt8(0, 2);   // color count (0 = no palette)
  e.writeUInt8(0, 3);   // reserved
  e.writeUInt16LE(1, 4); // color planes
  e.writeUInt16LE(32, 6); // bits per pixel
  e.writeUInt32LE(buf.length, 8);  // image data size
  e.writeUInt32LE(offsets[i], 12); // offset to image data
  return e;
});

const ico = Buffer.concat([icoHeader, ...dirEntries, ...pngBuffers]);
writeFileSync(join(dir, 'AppIcon.ico'), ico);
console.log(`✓ AppIcon.ico (${sizes.join(', ')} px)`);
