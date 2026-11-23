// Trusted Windows WebView parent for Bulwark's isolated sandbox frame.
// M6b-runtime-transport.md: messages enter native code only through this parent.
import { createSandboxParent } from './plugin-sandbox-parent.js';
import { decodePluginValue } from './plugin-value-codec.js';

const origin = 'https://plugin.invalid';
const managedOrigin = 'https://managed-plugin.invalid';
const hostUrl = origin + '/host/index.html';
const transportClosed = 'barbican-sandbox-transport-closed';
const maxWireBytes = 40 * 1024 * 1024;
const limits = { maxWireBytes, maxBinaryBytes: 25 * 1024 * 1024, maxStringBytes: 80 * 1024 * 1024 };

export function mountSandboxHost(host, document, webview, kind) {
  if (host.top !== host || host.location.href !== hostUrl || (kind !== 'background' && kind !== 'slot')) {
    throw new Error('Invalid sandbox host document');
  }
  if (typeof webview?.postMessage !== 'function' || typeof webview.addEventListener !== 'function') {
    throw new Error('Sandbox host requires a native WebView');
  }
  const declaredTier = document.body.dataset?.frameTier ?? '';
  if (declaredTier !== '' && declaredTier !== 'untrusted' && declaredTier !== 'privileged') throw new Error('Invalid sandbox frame tier');
  const tier = declaredTier === 'privileged' ? 'privileged' : 'untrusted';

  const frame = document.createElement('iframe');
  frame.setAttribute('sandbox', tier === 'privileged' ? 'allow-scripts allow-same-origin' : 'allow-scripts allow-forms');
  frame.setAttribute('referrerpolicy', 'no-referrer');
  frame.setAttribute('title', 'Add-on');
  frame.setAttribute('width', '100%');
  frame.setAttribute('height', '100%');
  frame.src = (tier === 'privileged' ? managedOrigin : origin) + '/sandbox/index.html' + (kind === 'slot' ? '?frame=slot' : '');
  document.body.appendChild(frame);

  const bridge = createSandboxParent({
    frame, tier, hostOrigin: origin,
    postNative: wire => webview.postMessage(wire),
    onActivity: () => {}, onOverflow: fault,
    maxMessageBytes: maxWireBytes,
  });

  const encoder = new TextEncoder();
  const fromFrame = event => { void bridge.receive(event); };
  const fromNative = event => {
    const wire = event.data;
    if (typeof wire !== 'string') return;
    if (wire.length > maxWireBytes) { fault(); return; }
    try {
      const message = decodePluginValue(encoder.encode(wire), limits);
      void bridge.send(message).catch(fault);
    } catch {
      fault();
    }
  };

  let closed = false;
  function fault() {
    if (closed) return;
    try { webview.postMessage(transportClosed); } finally { dispose(); }
  }
  function dispose() {
    if (closed) return;
    closed = true;
    host.removeEventListener('message', fromFrame);
    host.removeEventListener('pagehide', dispose);
    webview.removeEventListener('message', fromNative);
    bridge.dispose();
    frame.remove();
  }

  host.addEventListener('message', fromFrame);
  host.addEventListener('pagehide', dispose, { once: true });
  webview.addEventListener('message', fromNative);
  return dispose;
}

if (typeof window !== 'undefined') {
  mountSandboxHost(window, document, window.chrome?.webview, document.body.dataset.frameKind);
}
