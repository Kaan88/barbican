// Oracle: native JavaScript Date.toJSON used by Bulwark host-api.ts:427-445 storage JSON.stringify.
import { writeFile } from 'node:fs/promises';
const values = [-8640000000000000, -8639999999999999, -62167219200000, -62167219200001,
  -2203977600000, -1, 0, 1, 951782400000, 253402300799999, 253402300800000, 8640000000000000];
let seed = 8791;
for (let i = 0; i < 96; i++) {
  seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
  const days = seed % 200000000 - 100000000;
  seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
  values.push(days * 86400000 + seed % 86400000);
}
const vectors = values.map(milliseconds => ({ milliseconds, expected: JSON.parse(JSON.stringify(new Date(milliseconds))) }));
await writeFile(new URL('./plugin-date-golden.json', import.meta.url), JSON.stringify(vectors, null, 2) + '\n', 'utf8');
