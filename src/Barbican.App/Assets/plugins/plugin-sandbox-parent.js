// Host transport adaptation of Bulwark lib/plugin-sandbox/host-bridge.ts:427-477
// and runtime.tsx:1634-1639,1763-1788. Load in the trusted parent realm before plugins.
import { encodePluginValue, decodePluginValue } from './plugin-value-codec.js';

const inbound = new Set(['sandbox-ready','init-done','init-error','api-request','callback-invoke',
  'hook-result','theme-applied','slot-resize','frame-escape','slot-should-show-result',
  'floating-dropdown-open','floating-dropdown-update','floating-dropdown-close']);
const outbound = new Set(['init','api-response','callback-response','hook-invoke','locale-change',
  'theme-change','settings-change','host-change','props-update','slot-should-show',
  'stream-chunk','stream-end','stream-progress','floating-dropdown-select',
  'floating-dropdown-highlight','floating-dropdown-dismiss']);

// Call receive only with browser-created MessageEvents from postMessage. In particular,
// event.data must already have crossed the structured-clone boundary; arbitrary live
// objects or Proxies are not a safe input. Native must independently authenticate and
// validate every forwarded envelope and apply the operation-specific payload limits.
export function createSandboxParent({ frame, tier, hostOrigin, postNative, onActivity,
  maxMessageBytes = 40 * 1024 * 1024, maxBinaryBytes = 25 * 1024 * 1024 } = {}) {
  if (!frame?.contentWindow || !['untrusted','privileged'].includes(tier) ||
      typeof postNative !== 'function' || typeof onActivity !== 'function' ||
      !Number.isSafeInteger(maxMessageBytes) || maxMessageBytes < 1 ||
      !Number.isSafeInteger(maxBinaryBytes) || maxBinaryBytes < 0) throw new TypeError('Invalid sandbox parent configuration');
  let origin;
  try { const parsed = new URL(hostOrigin); if (parsed.protocol !== 'https:' || parsed.origin !== hostOrigin) throw new Error(); origin = parsed.origin; }
  catch { throw new TypeError('Invalid trusted origin'); }
  const target = frame.contentWindow;
  const expectedOrigin = tier === 'untrusted' ? 'null' : origin;
  const targetOrigin = tier === 'untrusted' ? '*' : origin;
  const limits = { maxWireBytes: maxMessageBytes, maxBinaryBytes, maxStringBytes: maxMessageBytes * 2 };
  const text = new TextDecoder();
  let disposed = false, ready = false, readyPending = false, initialized = false, initPending = false, port = null;
  let lastActivity = -Infinity, incoming = Promise.resolve(), outgoing = Promise.resolve();
  const current = () => !disposed && frame.contentWindow === target;
  const typeOf = value => {
    if (value === null || typeof value !== 'object' || Array.isArray(value)) return null;
    const descriptor = Object.getOwnPropertyDescriptor(value, 'type');
    return descriptor && Object.hasOwn(descriptor, 'value') && typeof descriptor.value === 'string' ? descriptor.value : null;
  };
  function dispose() {
    if (disposed) return;
    disposed = true;
    if (port) { port.onmessage = null; port.close(); port = null; }
  }
  function receive(event) {
    if (!current() || event.source !== target || event.origin !== expectedOrigin) return Promise.resolve(false);
    const kind = typeOf(event.data);
    if (!inbound.has(kind) || (kind === 'sandbox-ready' ? ready || readyPending : !ready || !initialized)) return Promise.resolve(false);
    if (kind === 'sandbox-ready') {
      if (event.ports?.length > 1) return Promise.resolve(false);
      readyPending = true;
    }
    // Snapshot synchronously before queueing: asynchronous Blob conversion must not
    // allow later messages to observe mutations or overtake earlier messages.
    const snapshot = encodePluginValue(event.data, limits).then(bytes => ({ bytes }), error => ({ error }));
    const ports = event.ports;
    const job = incoming.then(async () => {
      const result = await snapshot;
      if (result.error || !current()) return false;
      const bytes = result.bytes;
      if (kind === 'sandbox-ready') {
        ready = true;
        if (ports?.length === 1) {
          port = ports[0];
          port.onmessage = () => {
            const now = performance.now();
            if (current() && now - lastActivity >= 15000) { lastActivity = now; onActivity(); }
          };
          port.start?.();
        }
      }
      try { postNative(text.decode(bytes)); } catch { dispose(); return false; }
      return true;
    }).catch(() => false).finally(() => { if (kind === 'sandbox-ready') readyPending = false; });
    incoming = job;
    return job;
  }
  function send(message) {
    if (!current()) return Promise.reject(new Error('Sandbox is no longer active'));
    const kind = typeOf(message);
    if (!outbound.has(kind) || !ready || (kind === 'init' ? initialized || initPending : !initialized)) return Promise.reject(new Error('Invalid sandbox message or state'));
    if (kind === 'init') initPending = true;
    const snapshot = encodePluginValue(message, limits).then(bytes => ({ bytes }), error => ({ error }));
    const job = outgoing.then(async () => {
      const result = await snapshot;
      if (result.error) throw result.error;
      const bytes = result.bytes;
      if (!current()) throw new Error('Sandbox is no longer active');
      const clone = decodePluginValue(bytes, limits);
      const transfer = kind === 'stream-chunk' && clone.chunk instanceof ArrayBuffer ? [clone.chunk] : [];
      target.postMessage(clone, targetOrigin, transfer);
      if (kind === 'init') initialized = true;
    }).finally(() => { if (kind === 'init') initPending = false; });
    outgoing = job.catch(() => {});
    return job;
  }
  return Object.freeze({ receive, send, dispose });
}
