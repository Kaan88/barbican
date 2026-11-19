// Mirrors Bulwark lib/plugin-sandbox/protocol.ts:115-334 and runtime.tsx:368-407,646-666.
// The tagged v1 graph is the native transport adaptation, not Bulwark's postMessage wire.
// Input must be an authenticated structured-clone event value. Browser JavaScript cannot
// detect Proxy objects without invoking traps; never run this over arbitrary live objects.
// Buffers and graph descriptors are snapshotted before the first Blob await. No toJSON is called.
const defaults = Object.freeze({ maxWireBytes: 1048576, maxBinaryBytes: 524288, maxNodes: 4096,
  maxDepth: 64, maxStringBytes: 262144, maxSequenceLength: 4096, maxBigIntDigits: 4096 });
const utf8 = new TextEncoder();
const decoder = new TextDecoder('utf-8', { fatal: true });
const fields = ['id','kind','text','flag','bytes','links','keys','view','offset','length'];
const views = Object.freeze({ DataView, Int8: Int8Array, Uint8: Uint8Array, Uint8Clamped: Uint8ClampedArray,
  Int16: Int16Array, Uint16: Uint16Array, Int32: Int32Array, Uint32: Uint32Array,
  Float32: Float32Array, Float64: Float64Array, BigInt64: BigInt64Array, BigUint64: BigUint64Array,
  ...(typeof Float16Array === 'function' ? { Float16: Float16Array } : {}) });
const errorProtos = new Map([[Error.prototype,'Error'],[TypeError.prototype,'TypeError'],[RangeError.prototype,'RangeError'],
  [ReferenceError.prototype,'ReferenceError'],[SyntaxError.prototype,'SyntaxError'],[URIError.prototype,'URIError'],[EvalError.prototype,'EvalError']]);
// Captured before any plugin content runs, in the trusted parent realm only.
const ownDescriptor = Object.getOwnPropertyDescriptor;
const prototypeOf = Object.getPrototypeOf;
const applyIntrinsic = Reflect.apply;
const stackIntrinsic = ownDescriptor(new Error(), 'stack');
const prepareIntrinsic = ownDescriptor(Error, 'prepareStackTrace');
const errorToStringIntrinsic = ownDescriptor(Error.prototype, 'toString');
const getBufferLength = Object.getOwnPropertyDescriptor(ArrayBuffer.prototype, 'byteLength').get;
const ta = Object.getPrototypeOf(Uint8Array.prototype);
const taBuffer = Object.getOwnPropertyDescriptor(ta, 'buffer').get;
const taOffset = Object.getOwnPropertyDescriptor(ta, 'byteOffset').get;
const taLength = Object.getOwnPropertyDescriptor(ta, 'byteLength').get;
const dvBuffer = Object.getOwnPropertyDescriptor(DataView.prototype, 'buffer').get;
const dvOffset = Object.getOwnPropertyDescriptor(DataView.prototype, 'byteOffset').get;
const dvLength = Object.getOwnPropertyDescriptor(DataView.prototype, 'byteLength').get;

function fail(message) { throw new TypeError(`Plugin value: ${message}`); }
function policy(options) {
  const p = { ...defaults, ...options };
  for (const key of Object.keys(defaults)) if (!Number.isSafeInteger(p[key]) || p[key] < (['maxWireBytes','maxNodes','maxDepth','maxBigIntDigits'].includes(key) ? 1 : 0)) fail('invalid limit');
  if (p.maxDepth > 256) fail('invalid depth');
  for (const key of Object.keys(p)) if (!(key in defaults)) fail('unknown limit');
  return p;
}
function b64(bytes) {
  let result = '';
  const step = 0x6000; // divisible by three, so concatenation has one final padding only
  for (let i = 0; i < bytes.length; i += step) {
    let block = '';
    const end = Math.min(i + step, bytes.length);
    for (let j = i; j < end; j++) block += String.fromCharCode(bytes[j]);
    result += btoa(block);
  }
  return result;
}
function unb64(s, cap, counter) {
  if (typeof s !== 'string' || s.length % 4 !== 0 || !/^(?:[A-Za-z0-9+/]{4})*(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?$/.test(s)) fail('bad base64');
  const n = s.length / 4 * 3 - (s.endsWith('==') ? 2 : s.endsWith('=') ? 1 : 0);
  counter.n += n;
  if (n > cap || counter.n > cap) fail('byte budget');
  const decoded = atob(s);
  if (btoa(decoded) !== s) fail('noncanonical base64');
  const result = new Uint8Array(n);
  for (let i = 0; i < n; i++) result[i] = decoded.charCodeAt(i);
  return result;
}
function wellFormed(s) {
  for (let i = 0; i < s.length; i++) {
    const c = s.charCodeAt(i);
    if (c >= 0xd800 && c <= 0xdbff) { if (++i >= s.length || s.charCodeAt(i) < 0xdc00 || s.charCodeAt(i) > 0xdfff) return false; }
    else if (c >= 0xdc00 && c <= 0xdfff) return false;
  }
  return true;
}
function utf8Length(s) {
  let n = 0;
  for (let i=0;i<s.length;i++) {
    const c = s.charCodeAt(i);
    if (c < 0x80) n++; else if (c < 0x800) n += 2;
    else if (c >= 0xd800 && c <= 0xdbff && i+1 < s.length && s.charCodeAt(i+1) >= 0xdc00 && s.charCodeAt(i+1) <= 0xdfff) { n += 4; i++; }
    else n += 3;
  }
  return n;
}
function wireString(s) {
  if (s === null || wellFormed(s)) return s;
  const bytes = new Uint8Array(s.length * 2);
  for (let i = 0; i < s.length; i++) { const c = s.charCodeAt(i); bytes[i*2] = c & 255; bytes[i*2+1] = c >>> 8; }
  return { utf16: b64(bytes) };
}
function readString(v, p, strings) {
  if (v === null) return null;
  if (typeof v === 'string') { strings.n += v.length * 2; if (strings.n > p.maxStringBytes || !wellFormed(v)) fail('bad string'); return v; }
  exact(v, ['utf16']);
  const bytes = unb64(v.utf16, p.maxStringBytes, strings);
  if (bytes.length % 2) fail('odd UTF-16 byte count');
  let out = '';
  for (let i = 0; i < bytes.length; i += 2) out += String.fromCharCode(bytes[i] | bytes[i+1] << 8);
  return out;
}
function numberText(n) {
  if (Number.isNaN(n)) return 'NaN';
  if (n === Infinity) return 'Infinity';
  if (n === -Infinity) return '-Infinity';
  return Object.is(n, -0) ? '-0' : String(n);
}
function numberValue(s) {
  if (typeof s !== 'string' || !/^(?:NaN|Infinity|-Infinity|[+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)$/.test(s)) fail('bad number');
  return Number(s);
}
function exact(o, names) {
  if (o === null || typeof o !== 'object' || Array.isArray(o)) fail('object expected');
  const keys = Object.keys(o);
  if (keys.length !== names.length || keys.some(k => !names.includes(k))) fail('invalid fields');
}
function descriptor(object, key) {
  const d = Object.getOwnPropertyDescriptor(object, key);
  if (!d || !('value' in d)) fail('accessors are unsupported');
  return d.value;
}
function sameDescriptor(a,b) {
  if (!a || !b) return a === b;
  return a.value === b.value && a.get === b.get && a.set === b.set && a.configurable === b.configurable && a.enumerable === b.enumerable && a.writable === b.writable;
}
function errorData(error, name) {
  for (let o = error; o !== null; o = prototypeOf(o)) {
    const d = ownDescriptor(o, name);
    if (d) { if (!('value' in d) || typeof d.value !== 'string') fail('unsupported error accessor'); return d.value; }
  }
  return '';
}
function errorStack(error, proto) {
  const d = ownDescriptor(error, 'stack');
  if (!d) return { present:false, value:'' };
  if ('value' in d) { if (typeof d.value !== 'string') fail('unsupported error stack'); return { present:true, value:d.value }; }
  // Some engines expose a stable intrinsic stack getter. It may format name/message
  // and use Error.prepareStackTrace: reject if any route could invoke plugin code.
  if (!stackIntrinsic?.get || d.get !== stackIntrinsic.get || d.set !== stackIntrinsic.set ||
      !sameDescriptor(prepareIntrinsic, ownDescriptor(Error, 'prepareStackTrace')) ||
      !sameDescriptor(errorToStringIntrinsic, ownDescriptor(Error.prototype, 'toString')) ||
      (proto !== Error.prototype && prototypeOf(proto) !== Error.prototype) || prototypeOf(Error.prototype) !== Object.prototype) fail('unsupported error stack accessor');
  errorData(error,'name'); errorData(error,'message');
  const stack = applyIntrinsic(stackIntrinsic.get, error, []);
  if (typeof stack !== 'string') fail('unsupported error stack');
  return { present:true, value:stack };
}
function blank(id, kind) { return { id, kind, text:null, flag:false, bytes:null, links:null, keys:null, view:'DataView', offset:0, length:0 }; }

// Returns bytes; the caller owns the result. Blob conversion is the only asynchronous step.
export async function encodePluginValue(value, options = {}) {
  const p = policy(options), nodes = [], queue = [], seen = new Map(), blobs = [];
  let binary = 0, strings = 0, minimumWire = 0;
  function text(s) {
    strings += s.length * 2; if (strings > p.maxStringBytes) fail('string budget');
    minimumWire += wellFormed(s) ? utf8Length(s) : 4 * Math.ceil(s.length * 2 / 3);
    if (minimumWire > p.maxWireBytes) fail('wire budget');
    return wireString(s);
  }
  function reserveBinary(size) {
    binary += size; minimumWire += 4 * Math.ceil(size / 3);
    if (binary > p.maxBinaryBytes) fail('binary budget');
    if (minimumWire > p.maxWireBytes) fail('wire budget');
  }
  function add(v) {
    const ref = v !== null && (typeof v === 'object');
    if (ref && seen.has(v)) return seen.get(v);
    if (nodes.length >= p.maxNodes) fail('node budget');
    const id = nodes.length; nodes.push(null); queue.push([id, v]); if (ref) seen.set(v, id); return id;
  }
  const root = add(value);
  for (let q = 0; q < queue.length; q++) {
    const [id,v] = queue[q]; let n;
    if (v === undefined) n = blank(id,'Undefined');
    else if (v === null) n = blank(id,'Null');
    else if (typeof v === 'boolean') { n = blank(id,'Boolean'); n.flag = v; }
    else if (typeof v === 'number') { n = blank(id,'Number'); n.text = text(numberText(v)); }
    else if (typeof v === 'string') { n = blank(id,'String'); n.text = text(v); }
    else if (typeof v === 'bigint') { n = blank(id,'BigInt'); const s = v.toString(); if (s.length > p.maxBigIntDigits) fail('bigint budget'); n.text = text(s); }
    else if (typeof v !== 'object') fail('unsupported value');
    else {
      const proto = Object.getPrototypeOf(v);
      if (proto === ArrayBuffer.prototype) {
        const size = getBufferLength.call(v); reserveBinary(size);
        n = blank(id,'ArrayBuffer'); n.bytes = new Uint8Array(v).slice();
      } else if (ArrayBuffer.isView(v)) {
        const name = Object.keys(views).find(k => proto === views[k].prototype); if (!name) fail('unsupported view');
        const isData = name === 'DataView', buffer = (isData ? dvBuffer : taBuffer).call(v);
        n = blank(id,'View'); n.links = [add(buffer)]; n.view = name;
        n.offset = (isData ? dvOffset : taOffset).call(v); n.length = (isData ? dvLength : taLength).call(v);
      } else if (typeof Blob !== 'undefined' && proto === Blob.prototype) {
        const size = Object.getOwnPropertyDescriptor(Blob.prototype,'size').get.call(v);
        const type = Object.getOwnPropertyDescriptor(Blob.prototype,'type').get.call(v);
        reserveBinary(size);
        n = blank(id,'Blob'); n.text = text(type); n.bytes = new Uint8Array(0); blobs.push([n, v, size]);
      } else if (proto === Date.prototype) { n = blank(id,'Date'); n.text = text(numberText(Date.prototype.getTime.call(v))); }
      else if (proto === Map.prototype) {
        n = blank(id,'Map'); n.links = []; let count = 0;
        for (const [k,x] of Map.prototype.entries.call(v)) { if (++count > p.maxSequenceLength) fail('sequence budget'); n.links.push(add(k), add(x)); }
      } else if (proto === Set.prototype) {
        n = blank(id,'Set'); n.links = []; let count = 0;
        for (const x of Set.prototype.values.call(v)) { if (++count > p.maxSequenceLength) fail('sequence budget'); n.links.push(add(x)); }
      } else if (errorProtos.has(proto)) {
        if (Reflect.ownKeys(v).some(k => typeof k !== 'string' || !['name','message','stack','cause'].includes(k))) fail('unsupported error property');
        const name = errorData(v,'name'), message = errorData(v,'message');
        const stack = errorStack(v,proto);
        n = blank(id,'Error'); n.keys = [text(name), text(message), text(stack.value)]; n.flag = stack.present;
        n.links = Object.hasOwn(v,'cause') ? [add(descriptor(v,'cause'))] : [];
      } else if (Array.isArray(v) && proto === Array.prototype) {
        if (v.length > p.maxSequenceLength) fail('sequence budget'); n = blank(id,'Array'); n.links = [];
        const keys = Reflect.ownKeys(v); if (keys.some(k => k !== 'length' && (typeof k !== 'string' || !/^(0|[1-9]\d*)$/.test(k) || Number(k) >= v.length))) fail('unsupported array property');
        for (let i = 0; i < v.length; i++) n.links.push(add(descriptor(v,String(i)))); // holes/accessors fail, never coerced
      } else if (proto === Object.prototype || proto === null) {
        n = blank(id,'Object'); n.keys = []; n.links = [];
        const keys = Reflect.ownKeys(v); if (keys.length > p.maxSequenceLength) fail('sequence budget');
        for (const k of keys) { if (typeof k !== 'string') fail('symbol key'); const d = Object.getOwnPropertyDescriptor(v,k); if (!d || !('value' in d) || !d.enumerable) fail('unsupported object property'); n.keys.push(text(k)); n.links.push(add(d.value)); }
      } else fail('unsupported prototype');
    }
    nodes[id] = n;
  }
  validateGraph(nodes, root, p, true);
  if (4 * Math.ceil(binary / 3) > p.maxWireBytes) fail('wire budget');
  // Blob is immutable; every mutable buffer above was already copied before this await.
  for (const [node, blob, size] of blobs) { const bytes = new Uint8Array(await Blob.prototype.arrayBuffer.call(blob)); if (bytes.length !== size) fail('blob changed'); node.bytes = bytes; }
  let minimum = 0;
  for (const n of nodes) { if (n.bytes) minimum += 4 * Math.ceil(n.bytes.length / 3); if (typeof n.text === 'string') minimum += utf8Length(n.text); if (minimum > p.maxWireBytes) fail('wire budget'); }
  for (const n of nodes) if (n.bytes instanceof Uint8Array) n.bytes = b64(n.bytes);
  const result = utf8.encode(safeJson({ version:1, root, nodes }, p.maxWireBytes));
  if (result.length > p.maxWireBytes) fail('wire budget');
  return result;
}
// Serialize only codec-created records and arrays; never consult a toJSON property.
function safeJson(root, maxBytes) {
  const parts = []; let size = 0;
  function add(text) { size += utf8Length(text); if (size > maxBytes) fail('wire budget'); parts.push(text); }
  function write(v) {
    if (v === null) { add('null'); return; }
    if (typeof v === 'string') { add(JSON.stringify(v)); return; }
    if (typeof v === 'number' || typeof v === 'boolean') { add(String(v)); return; }
    if (Array.isArray(v)) { add('['); for (let i=0;i<v.length;i++) { if (i) add(','); write(v[i]); } add(']'); return; }
    add('{'); const keys = Object.keys(v);
    for (let i=0;i<keys.length;i++) { if (i) add(','); add(JSON.stringify(keys[i])); add(':'); write(descriptor(v,keys[i])); }
    add('}');
  }
  write(root); return parts.join('');
}

// JSON.parse silently overwrites duplicate keys. Parse this small fixed-depth wire grammar
// into null-prototype records, rejecting duplicates before any node is trusted.
function parseWire(text, p) {
  let i = 0, tokens = 0;
  const cap = Math.max(p.maxNodes, p.maxSequenceLength * 2, 16);
  function ws() { while (i < text.length && /[\x20\t\r\n]/.test(text[i])) i++; }
  function str(maxChars) {
    const start = i++; let count = 0;
    while (i < text.length) {
      const c = text[i++];
      if (c === '"') { try { return JSON.parse(text.slice(start,i)); } catch { fail('invalid JSON string'); } }
      if (c === '\\') { if (i >= text.length) fail('truncated JSON string'); const escape = text[i++]; if (escape === 'u') i += 4; }
      else if (c.charCodeAt(0) < 32) fail('invalid JSON string');
      if (++count > maxChars) fail('string allocation budget');
    }
    fail('truncated JSON string');
  }
  function stringCap(context) {
    if (context === 'text' || context === 'keys') return Math.floor(p.maxStringBytes / 2);
    if (context === 'bytes') return 4 * Math.ceil(p.maxBinaryBytes / 3);
    if (context === 'utf16') return 4 * Math.ceil(p.maxStringBytes / 3);
    return Math.min(p.maxWireBytes, 256);
  }
  function read(depth, context) {
    ws(); if (depth > 8 || ++tokens > p.maxWireBytes) fail('JSON complexity');
    if (text[i] === '"') return str(stringCap(context));
    if (text[i] === '{') {
      i++; ws(); const o = Object.create(null); let count = 0;
      if (text[i] === '}') { i++; return o; }
      while (true) {
        ws(); if (text[i] !== '"') fail('invalid object'); const k = str(256);
        if (++count > cap || Object.hasOwn(o,k)) fail('duplicate or excessive fields');
        ws(); if (text[i++] !== ':') fail('missing colon');
        Object.defineProperty(o,k,{ value:read(depth+1,k), enumerable:true, configurable:true, writable:true });
        ws(); if (text[i] === '}') { i++; return o; } if (text[i++] !== ',') fail('invalid object');
      }
    }
    if (text[i] === '[') {
      i++; ws(); const a = []; if (text[i] === ']') { i++; return a; }
      while (true) { if (a.length >= cap) fail('sequence budget'); a.push(read(depth+1,context)); ws(); if (text[i] === ']') { i++; return a; } if (text[i++] !== ',') fail('invalid array'); }
    }
    for (const [word,value] of [['null',null],['true',true],['false',false]]) if (text.startsWith(word,i)) { i += word.length; return value; }
    const m = /^-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?/.exec(text.slice(i));
    if (!m) fail('invalid JSON'); i += m[0].length; const n = Number(m[0]); if (!Number.isFinite(n)) fail('invalid JSON number'); return n;
  }
  const result = read(1,''); ws(); if (i !== text.length) fail('trailing JSON'); return result;
}
function integer(n) { return Number.isSafeInteger(n) && n >= -2147483648 && n <= 2147483647; }
function validDefaultFields(n) { return n.view === 'DataView' && n.offset === 0 && n.length === 0; }
function validateGraph(nodes, root, p, encoding = false) {
  if (!Array.isArray(nodes) || nodes.length > p.maxNodes || !integer(root) || root < 0 || root >= nodes.length) fail('invalid root');
  const byId = new Array(nodes.length), binary = { n:0 }, strings = { n:0 };
  for (const raw of nodes) {
    exact(raw, fields);
    if (!integer(raw.id) || raw.id < 0 || raw.id >= nodes.length || byId[raw.id]) fail('duplicate node');
    if (typeof raw.kind !== 'string' || typeof raw.view !== 'string' || !Object.hasOwn(views,raw.view) || typeof raw.flag !== 'boolean' || !integer(raw.offset) || !integer(raw.length)) fail('bad node metadata');
    const n = { ...raw }; n.text = readString(raw.text,p,strings);
    if (raw.bytes === null) n.data = null;
    else if (encoding && raw.bytes instanceof Uint8Array) { binary.n += raw.bytes.length; if (binary.n > p.maxBinaryBytes) fail('binary budget'); n.data = raw.bytes; }
    else n.data = unb64(raw.bytes,p.maxBinaryBytes,binary);
    if (raw.links !== null && (!Array.isArray(raw.links) || raw.links.length > p.maxSequenceLength*2 || raw.links.some(x => !integer(x) || x < 0 || x >= nodes.length))) fail('bad links');
    if (raw.keys !== null && (!Array.isArray(raw.keys) || raw.keys.length > p.maxSequenceLength)) fail('bad keys');
    n.keys = raw.keys === null ? null : raw.keys.map(x => { const key = readString(x,p,strings); if (key === null) fail('null key'); return key; });
    byId[n.id] = n;
  }
  if (byId.some(n => !n)) fail('missing node');
  for (const n of byId) validateShape(n, byId, p);
  const depths = new Array(nodes.length).fill(0), active = new Set(); let visits = 0;
  function walk(id, depth) {
    if (active.has(id)) return;
    if (depth > p.maxDepth || ++visits > p.maxNodes*p.maxDepth) fail('graph complexity');
    if (depth <= depths[id]) return; depths[id] = depth; active.add(id);
    for (const x of byId[id].links ?? []) walk(x,depth+1);
    active.delete(id);
  }
  walk(root,1); for (let i=0;i<byId.length;i++) if (!depths[i]) walk(i,1);
  return byId;
}
function validateShape(n, nodes, p) {
  const empty = n.data === null && n.links === null && n.keys === null;
  let ok = false;
  switch (n.kind) {
    case 'Undefined': case 'Null': ok = n.text === null && empty && !n.flag; break;
    case 'Boolean': ok = n.text === null && empty; break;
    case 'Number': case 'Date': numberValue(n.text); ok = empty && !n.flag; break;
    case 'BigInt': ok = typeof n.text === 'string' && n.text.length <= p.maxBigIntDigits && /^[+-]?\d+$/.test(n.text) && empty && !n.flag; break;
    case 'String': ok = typeof n.text === 'string' && empty && !n.flag; break;
    case 'ArrayBuffer': ok = n.data !== null && n.text === null && n.links === null && n.keys === null && !n.flag; break;
    case 'Blob': ok = n.data !== null && typeof n.text === 'string' && n.links === null && n.keys === null && !n.flag; break;
    case 'View': {
      const sizes = { Int16:2,Uint16:2,Float16:2,Int32:4,Uint32:4,Float32:4,Float64:8,BigInt64:8,BigUint64:8 };
      const size = sizes[n.view] ?? 1, base = n.links?.length === 1 ? nodes[n.links[0]] : null;
      ok = n.data === null && n.text === null && n.keys === null && !!base && base.kind === 'ArrayBuffer' && base.data !== null && n.offset >= 0 && n.length >= 0 && n.offset+n.length <= base.data.length && n.offset%size === 0 && n.length%size === 0 && !n.flag; break;
    }
    case 'Object': ok = n.data === null && n.text === null && Array.isArray(n.keys) && Array.isArray(n.links) && n.keys.length === n.links.length && n.links.length <= p.maxSequenceLength && new Set(n.keys).size === n.keys.length && !n.flag; break;
    case 'Array': case 'Set': ok = n.data === null && n.text === null && n.keys === null && Array.isArray(n.links) && n.links.length <= p.maxSequenceLength && !n.flag; break;
    case 'Map': ok = n.data === null && n.text === null && n.keys === null && Array.isArray(n.links) && n.links.length%2 === 0 && n.links.length/2 <= p.maxSequenceLength && !n.flag; break;
    case 'Error': ok = n.data === null && n.text === null && n.keys?.length === 3 && Array.isArray(n.links) && n.links.length <= 1; break;
    default: fail('unknown kind');
  }
  if (!ok || (n.kind !== 'View' && !validDefaultFields(n))) fail('invalid node shape');
}

// Returns a fresh graph. No untrusted constructors, accessors, toJSON, or host objects are called.
export function decodePluginValue(bytes, options = {}) {
  const p = policy(options);
  if (!(bytes instanceof Uint8Array) || bytes.byteLength > p.maxWireBytes) fail('wire budget');
  let wire; try { wire = parseWire(decoder.decode(bytes),p); } catch (e) { if (e instanceof TypeError) throw e; fail('malformed UTF-8 or JSON'); }
  exact(wire,['version','root','nodes']); if (wire.version !== 1) fail('unsupported version');
  const nodes = validateGraph(wire.nodes,wire.root,p), values = new Array(nodes.length);
  const errorTypes = { Error, TypeError, RangeError, ReferenceError, SyntaxError, URIError, EvalError };
  for (const n of nodes) {
    switch (n.kind) {
      case 'Undefined': values[n.id] = undefined; break;
      case 'Null': values[n.id] = null; break;
      case 'Boolean': values[n.id] = n.flag; break;
      case 'Number': values[n.id] = numberValue(n.text); break;
      case 'String': values[n.id] = n.text; break;
      case 'BigInt': values[n.id] = BigInt(n.text); break;
      case 'Date': { const ms = numberValue(n.text); if (Object.is(ms,-0) || (!Number.isFinite(ms) && !Number.isNaN(ms)) || (Number.isFinite(ms) && (!Number.isInteger(ms) || Math.abs(ms) > 8640000000000000))) fail('invalid date'); values[n.id] = new Date(ms); break; }
      case 'ArrayBuffer': values[n.id] = n.data.slice().buffer; break;
      case 'Blob': { const blob = new Blob([n.data], {type:n.text}); if (blob.type !== n.text) fail('invalid blob MIME type'); values[n.id] = blob; break; }
      case 'Array': values[n.id] = []; break;
      case 'Object': values[n.id] = {}; break;
      case 'Map': values[n.id] = new Map(); break;
      case 'Set': values[n.id] = new Set(); break;
      case 'Error': { const Ctor = Object.hasOwn(errorTypes,n.keys[0]) ? errorTypes[n.keys[0]] : Error; const e = new Ctor(n.keys[1]); Object.defineProperty(e,'name',{value:n.keys[0],writable:true,configurable:true}); if (n.flag) Object.defineProperty(e,'stack',{value:n.keys[2],writable:true,configurable:true}); else delete e.stack; values[n.id] = e; break; }
      case 'View': break;
    }
  }
  for (const n of nodes) if (n.kind === 'View') {
    const Ctor = views[n.view], buffer = values[n.links[0]];
    values[n.id] = n.view === 'DataView' ? new DataView(buffer,n.offset,n.length) : new Ctor(buffer,n.offset,n.length/Ctor.BYTES_PER_ELEMENT);
  }
  for (const n of nodes) {
    if (n.kind === 'Array') for (const id of n.links) values[n.id].push(values[id]);
    else if (n.kind === 'Object') for (let i=0;i<n.links.length;i++) Object.defineProperty(values[n.id],n.keys[i],{value:values[n.links[i]],enumerable:true,configurable:true,writable:true});
    else if (n.kind === 'Map') for (let i=0;i<n.links.length;i+=2) values[n.id].set(values[n.links[i]],values[n.links[i+1]]);
    else if (n.kind === 'Set') for (const id of n.links) values[n.id].add(values[id]);
    else if (n.kind === 'Error' && n.links.length) Object.defineProperty(values[n.id],'cause',{value:values[n.links[0]],writable:true,configurable:true});
  }
  return values[wire.root];
}
