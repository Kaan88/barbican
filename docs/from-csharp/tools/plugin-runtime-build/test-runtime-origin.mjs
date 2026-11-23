// Run against the patched, isolated runtime.tsx: node test-runtime-origin.mjs <runtime.tsx>.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const source = readFileSync(process.argv[2], 'utf8');
const start = source.indexOf('function resolveHost():');
const end = source.indexOf('\n}', start);
assert.ok(start >= 0 && end > start);
const block = source.slice(start, end + 2).replace('function resolveHost(): { window: Window; origin: string } | null', 'function resolveHost()');
const resolve = new Function('window', 'document', `${block}; return resolveHost();`);
const parent = {};
const check = (origin, values) => resolve({ parent, location: { origin } }, {
  querySelectorAll: () => values.map(value => ({ getAttribute: name => name === 'content' ? value : null })),
});
assert.equal(check('https://plugin.invalid', []).origin, 'https://plugin.invalid');
assert.equal(check('https://managed-plugin.invalid', ['https://plugin.invalid']).origin, 'https://plugin.invalid');
assert.equal(check('https://managed-plugin.invalid', []), null);
assert.equal(check('https://managed-plugin.invalid', ['https://evil.invalid']), null);
assert.equal(check('https://managed-plugin.invalid', ['https://plugin.invalid', 'https://plugin.invalid']), null);
assert.equal(check('null', []), null);
console.log('Runtime parent-origin checks passed.');
