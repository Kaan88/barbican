import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { encodePluginValue, decodePluginValue } from '../../src/Barbican.App/Assets/plugins/plugin-value-codec.js';
const here = new URL('.', import.meta.url);
const jsPath = new URL('plugin-value-js-golden.json', here);
const csPath = new URL('plugin-value-csharp-golden.json', here);
const asJson = bytes => JSON.parse(new TextDecoder().decode(bytes));
const toBytes = x => new TextEncoder().encode(JSON.stringify(x));
function jsValue() {
  const bytes = new Uint8Array([10,20,30,40]).buffer;
  const root = { name:'JS fixture', buffer:bytes, view:new Uint8Array(bytes,1,2), empty:new DataView(bytes,4,0),
    missing:undefined, nul:null, negativeZero:-0, big:12345678901234567890n, weird:'\ud800\udc00\ud800\0',
    blob:new Blob([new Uint8Array([8,9])],{type:'text/plain'}), date:new Date(1234), map:new Map(), set:new Set() };
  root.self = root; root.map.set('root',root); root.set.add(root.view);
  const error = new TypeError('fixture error'); Object.defineProperty(error,'stack',{value:'fixture stack',configurable:true,writable:true}); error.cause = root; root.error = error;
  Object.defineProperty(root,'__proto__',{value:'safe',enumerable:true,writable:true,configurable:true});
  return root;
}
function csharpWire() {
  function node(id, kind, fields={}) { return {id,kind,text:null,flag:false,bytes:null,links:null,keys:null,view:'DataView',offset:0,length:0,...fields}; }
  return {version:1,root:0,nodes:[
    node(0,'Object',{keys:['name','data','view','self','undef','special','blob','error'],links:[1,2,3,0,4,5,6,7]}),
    node(1,'String',{text:'C# fixture'}), node(2,'ArrayBuffer',{bytes:'AQIDBA=='}),
    node(3,'View',{links:[2],view:'Uint8',offset:1,length:2}), node(4,'Undefined'),
    node(5,'BigInt',{text:'12345678901234567890'}), node(6,'Blob',{text:'text/plain',bytes:'CAk='}),
    node(7,'Error',{keys:['TypeError','fixture error','fixture stack'],flag:true,links:[0]}) ]};
}
if (process.argv.includes('--write-fixtures')) {
  await writeFile(jsPath, JSON.stringify(asJson(await encodePluginValue(jsValue())))+'\n');
  await writeFile(csPath, JSON.stringify(csharpWire())+'\n');
}
const jsGolden = JSON.parse(await readFile(jsPath,'utf8'));
const csGolden = JSON.parse(await readFile(csPath,'utf8'));
assert.deepEqual(asJson(await encodePluginValue(jsValue())), jsGolden);
const c = decodePluginValue(toBytes(csGolden));
assert.equal(c.name,'C# fixture'); assert.equal(c.self,c); assert.equal(c.view.buffer,c.data);
assert.deepEqual([...c.view],[2,3]); assert.equal(c.undef,undefined); assert.equal(c.special,12345678901234567890n);
assert.deepEqual([...new Uint8Array(await c.blob.arrayBuffer())],[8,9]); assert.ok(c.error instanceof TypeError); assert.equal(c.error.cause,c);
const j = decodePluginValue(toBytes(jsGolden));
assert.equal(j.self,j); assert.equal(j.map.get('root'),j); assert.equal(j.view.buffer,j.buffer);
assert.equal(j.empty.byteLength,0); assert.equal(j.weird,'\ud800\udc00\ud800\0'); assert.ok(Object.is(j.negativeZero,-0));
assert.equal(j.__proto__,'safe'); assert.equal(Object.getPrototypeOf(j),Object.prototype);
assert.equal(j.set.has(j.view),true); assert.equal(j.date.getTime(),1234); assert.equal(j.error.cause,j);
const x = {}; x.self=x; const cycle = decodePluginValue(await encodePluginValue(x)); assert.equal(cycle.self,cycle);
await assert.rejects(() => encodePluginValue({ get unsafe() { throw new Error('getter ran'); } }), /accessors|unsupported object/);
await assert.rejects(() => encodePluginValue({ f() {} }), /unsupported value/);
await assert.rejects(() => encodePluginValue(new Uint8Array(1000).buffer,{maxWireBytes:16}), /wire budget/);
const bad = structuredClone(csGolden); bad.nodes[3].length = 9; assert.throws(() => decodePluginValue(toBytes(bad)), /invalid node shape/);
const duplicate = new TextEncoder().encode('{"version":1,"version":1,"root":0,"nodes":[]}'); assert.throws(() => decodePluginValue(duplicate), /duplicate/);
const raw = new Uint8Array([1,2,3,4]); const asyncValue = { blob:new Blob(['x']), buffer:raw.buffer };
const pending = encodePluginValue(asyncValue); raw[0] = 99; const snap = decodePluginValue(await pending); assert.equal(new Uint8Array(snap.buffer)[0],1);
// Extra adversarial cases: no inherited toJSON, duplicate escaped keys, strict unsupported values.
let getterRan = false;
const accessor = {}; Object.defineProperty(accessor,'x',{enumerable:true,get() { getterRan = true; return 1; }});
await assert.rejects(() => encodePluginValue(accessor), /unsupported object property/); assert.equal(getterRan,false);
Object.prototype.toJSON = function () { throw new Error('toJSON ran'); };
try { const plain = await encodePluginValue({ x:1 }); assert.equal(decodePluginValue(plain).x,1); } finally { delete Object.prototype.toJSON; }
await assert.rejects(() => encodePluginValue(/regex/), /unsupported prototype/);
await assert.rejects(() => encodePluginValue(new Array(2)), /accessors/);
const dupEscaped = new TextEncoder().encode('{"version":1,"\\u0076ersion":1,"root":0,"nodes":[]}');
assert.throws(() => decodePluginValue(dupEscaped), /duplicate/);
const wrong = structuredClone(csGolden); wrong.nodes[3].view = 'NoSuchArray'; assert.throws(() => decodePluginValue(toBytes(wrong)), /bad node metadata/);
const privateGraph = { public:{ok:'yes'}, private:'private-sibling-secret' };
assert.equal(new TextDecoder().decode(await encodePluginValue(privateGraph.public)).includes('private-sibling-secret'),false);
const ee = new TypeError('bad'); Object.defineProperty(ee,'stack',{value:'sample',writable:true,configurable:true}); ee.cause = ee;
const ee2 = decodePluginValue(await encodePluginValue(ee)); assert.ok(ee2 instanceof TypeError); assert.equal(ee2.cause,ee2); assert.equal(ee2.stack,'sample');
const weirdKey = {}; Object.defineProperty(weirdKey,'\ud800',{value:'\udc00',enumerable:true});
const weirdCopy = decodePluginValue(await encodePluginValue(weirdKey)); assert.equal(weirdCopy['\ud800'],'\udc00');
assert.throws(() => decodePluginValue(toBytes(csGolden),{maxStringBytes:1}), /budget/);
assert.throws(() => decodePluginValue(toBytes(csGolden),{maxBinaryBytes:1}), /budget/);
for (const text of ['Infinity','-Infinity','0.5','8640000000000001','-0']) {
  const invalidDate = structuredClone(csGolden); invalidDate.nodes[1].kind = 'Date'; invalidDate.nodes[1].text = text;
  assert.throws(() => decodePluginValue(toBytes(invalidDate)), /invalid date/);
}
for (const type of ['Text/Plain','text/\u0001plain','text/é']) {
  const invalidBlob = structuredClone(csGolden); invalidBlob.nodes[6].text = type;
  assert.throws(() => decodePluginValue(toBytes(invalidBlob)), /invalid blob MIME type/);
}
// Only a captured intrinsic stack accessor may run; arbitrary formatting hooks and getters fail closed.
const nativeError = structuredClone(new TypeError('cloned error'));
const nativeCopy = decodePluginValue(await encodePluginValue(nativeError));
assert.equal(nativeCopy.name,'TypeError'); assert.equal(nativeCopy.message,'cloned error'); assert.equal(typeof nativeCopy.stack,'string');
let invoked = false;
const hostileStack = new Error('x'); Object.defineProperty(hostileStack,'stack',{get() { invoked=true; return 'bad'; },configurable:true});
await assert.rejects(() => encodePluginValue(hostileStack), /unsupported error stack accessor/); assert.equal(invoked,false);
const hostileName = new Error('x'); Object.defineProperty(hostileName,'name',{get() { invoked=true; return 'bad'; },configurable:true});
await assert.rejects(() => encodePluginValue(hostileName), /unsupported error accessor/); assert.equal(invoked,false);
const originalPrepare = Object.getOwnPropertyDescriptor(Error,'prepareStackTrace');
Object.defineProperty(Error,'prepareStackTrace',{value() { invoked=true; return 'bad'; },configurable:true,writable:true});
try { await assert.rejects(() => encodePluginValue(new Error('x')), /unsupported error stack accessor/); assert.equal(invoked,false); }
finally { if (originalPrepare) Object.defineProperty(Error,'prepareStackTrace',originalPrepare); else delete Error.prepareStackTrace; }
console.log('plugin-value codec: JS/C# vectors and adversarial checks passed');
