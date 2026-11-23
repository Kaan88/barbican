import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { test } from 'node:test';

const prelude = await readFile(new URL('../../src/Barbican.App/Assets/plugins/plugin-sandbox-network.js', import.meta.url));
const moduleUrl = 'data:text/javascript;base64,' + prelude.toString('base64');
const report = disposition => ({ type: 'connection-allowlist', body: { connection: 'webrtc', disposition } });

test('child imports the SDK only after a local enforcement report', async () => {
  const originals = { document: globalThis.document, ReportingObserver: globalThis.ReportingObserver,
    RTCPeerConnection: globalThis.RTCPeerConnection };
  let observed = false;
  let closed = false;
  let disconnected = false;
  let reports = [];
  globalThis.sdkStarted = false;
  globalThis.document = {
    querySelector: () => ({ content: 'data:text/javascript,globalThis.sdkStarted%20%3D%20true' }),
  };
  globalThis.RTCPeerConnection = class {
    constructor(options) {
      assert.deepEqual(options, { iceServers: [] });
      assert.equal(observed, true);
    }
    close() { closed = true; }
  };
  try {
    globalThis.ReportingObserver = undefined;
    await import(moduleUrl + '#unsupported');
    assert.equal(globalThis.sdkStarted, false);
    assert.equal(closed, false);

    globalThis.ReportingObserver = class {
      observe() { observed = true; }
      takeRecords() { return reports; }
      disconnect() { disconnected = true; }
    };
    reports = [report('report')];
    await import(moduleUrl + '#report-only');
    assert.equal(closed, true);
    assert.equal(disconnected, true);
    assert.equal(globalThis.sdkStarted, false);

    reports = [report('enforce')];
    await import(moduleUrl + '#enforced');
    assert.equal(globalThis.sdkStarted, true);
  } finally {
    Object.assign(globalThis, originals);
    delete globalThis.sdkStarted;
  }
});
