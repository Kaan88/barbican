import test from 'node:test';
import assert from 'node:assert/strict';
import { mountSandboxHost } from '../../src/Barbican.App/Assets/plugins/plugin-sandbox-host.js';
import { decodePluginValue, encodePluginValue } from '../../src/Barbican.App/Assets/plugins/plugin-value-codec.js';

function events() {
  const handlers = new Map();
  return {
    addEventListener: (name, handler) => handlers.set(name, handler),
    removeEventListener: name => handlers.delete(name),
    raise: (name, event) => handlers.get(name)?.(event),
  };
}

function setup(kind, tier = '') {
  const writes = [], childMessages = [], attributes = new Map();
  const host = { ...events(), location: { href: 'https://plugin.invalid/host/index.html' } };
  host.top = host;
  const webview = { ...events(), postMessage: value => writes.push(value) };
  const child = { postMessage: (...args) => childMessages.push(args) };
  let removed = false;
  const frame = {
    contentWindow: child,
    setAttribute: (name, value) => attributes.set(name, value),
    remove: () => { removed = true; },
  };
  const document = { createElement: name => {
    assert.equal(name, 'iframe');
    return frame;
  }, body: { dataset: { frameTier: tier }, appendChild: element => assert.strictEqual(element, frame) } };
  return { host, document, webview, child, frame, attributes, writes, childMessages,
    mount: () => mountSandboxHost(host, document, webview, kind), removed: () => removed };
}

const turn = () => new Promise(resolve => setImmediate(resolve));

test('trusted host mounts only the fixed untrusted background and slot routes', () => {
  for (const kind of ['background', 'slot']) {
    const fixture = setup(kind);
    fixture.mount();
    assert.equal(fixture.attributes.get('sandbox'), 'allow-scripts allow-forms');
    assert.equal(fixture.frame.src, 'https://plugin.invalid/sandbox/index.html' + (kind === 'slot' ? '?frame=slot' : ''));
  }
  const nested = setup('background');
  nested.host.top = {};
  assert.throws(nested.mount);
  const wrongUrl = setup('background');
  wrongUrl.host.location.href += '?slot=yes';
  assert.throws(wrongUrl.mount);
  assert.throws(setup('privileged').mount);
});

test('managed host uses only the fixed cross-origin frame and exact message origin', async () => {
  for (const kind of ['background', 'slot']) {
    const f = setup(kind, 'privileged');
    f.mount();
    assert.equal(f.attributes.get('sandbox'), 'allow-scripts allow-same-origin');
    assert.equal(f.frame.src, 'https://managed-plugin.invalid/sandbox/index.html' + (kind === 'slot' ? '?frame=slot' : ''));
    for (const event of [
      { source: f.child, origin: 'null' },
      { source: f.child, origin: 'https://plugin.invalid' },
      { source: {}, origin: 'https://managed-plugin.invalid' },
    ]) f.host.raise('message', { ...event, data: { type: 'sandbox-ready' }, ports: [] });
    await turn();
    assert.deepEqual(f.writes, []);
    f.host.raise('message', { source: f.child, origin: 'https://managed-plugin.invalid', data: { type: 'sandbox-ready' }, ports: [] });
    await turn();
    assert.equal(f.writes.length, 1);
    const wire = new TextDecoder().decode(await encodePluginValue({ type: 'init', payload: {} }));
    f.webview.raise('message', { data: wire });
    await turn();
    assert.equal(f.childMessages[0][1], 'https://managed-plugin.invalid');
  }
  assert.throws(setup('slot', 'unknown').mount);
});

test('parent bridges string wire only for the selected frame and removes routes on pagehide', async () => {
  const f = setup('slot');
  f.mount();
  f.host.raise('message', { source: {}, origin: 'null', data: { type: 'sandbox-ready' }, ports: [] });
  await turn();
  assert.deepEqual(f.writes, []);
  f.host.raise('message', { source: f.child, origin: 'null', data: { type: 'sandbox-ready' }, ports: [] });
  await turn();
  assert.equal(typeof f.writes[0], 'string');
  const ready = decodePluginValue(new TextEncoder().encode(f.writes[0]));
  assert.equal(ready.type, 'sandbox-ready');

  const init = { type: 'init', payload: { code: 'export default {}' } };
  const wire = new TextDecoder().decode(await encodePluginValue(init));
  f.webview.raise('message', { data: init });
  assert.deepEqual(f.childMessages, []);
  f.webview.raise('message', { data: wire });
  await turn();
  assert.equal(f.childMessages.length, 1);
  assert.equal(f.childMessages[0][0].type, 'init');
  assert.equal(f.childMessages[0][1], '*');

  f.host.raise('pagehide');
  assert.equal(f.removed(), true);
  f.webview.raise('message', { data: wire });
  await turn();
  assert.equal(f.childMessages.length, 1);
});

test('an authenticated saturated queue reports transport closure exactly once', async () => {
  const f = setup('background');
  f.mount();
  const message = (data) => ({ source: f.child, origin: 'null', data, ports: [] });
  f.host.raise('message', message({ type: 'sandbox-ready' }));
  await turn();
  const wire = new TextDecoder().decode(await encodePluginValue({ type: 'init', payload: {} }));
  f.webview.raise('message', { data: wire });
  await turn();

  for (let n = 0; n < 3; n++) f.host.raise('message', message({ type: 'api-request', id: String(n) }));
  await turn();
  assert.equal(f.writes.filter(wire => wire === 'barbican-sandbox-transport-closed').length, 1);
  assert.equal(f.removed(), true);
  f.webview.raise('message', { data: 'not a valid wire payload' });
  assert.equal(f.writes.length, 2);
});

test('native send rejection closes both sides observably', async () => {
  const f = setup('slot');
  f.mount();
  f.webview.raise('message', { data: 'not a valid wire payload' });
  f.webview.raise('message', { data: 'another invalid message' });
  await turn();
  assert.deepEqual(f.writes, ['barbican-sandbox-transport-closed']);
  assert.equal(f.removed(), true);
});
