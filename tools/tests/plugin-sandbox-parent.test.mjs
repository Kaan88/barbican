// Source-derived red acceptance: Bulwark protocol.ts:115-334; host-bridge.ts:427-477,655-666; runtime.tsx:1634-1639,1763-1788.
// Imports the real future parent entry point. No alternate runtime is implemented by these test doubles.
import test from 'node:test';
import assert from 'node:assert/strict';
import { createSandboxParent } from '../../src/Barbican.App/Assets/plugins/plugin-sandbox-parent.js';
import { decodePluginValue } from '../../src/Barbican.App/Assets/plugins/plugin-value-codec.js';

function rig({ tier='untrusted', hostOrigin='https://plugin.invalid' } = {}) {
  const sent = [], native = [], activities = [];
  const window = { postMessage: (...args) => sent.push(args) };
  const frame = { contentWindow: window };
  const bridge = createSandboxParent({ frame, tier, hostOrigin, postNative: wire => { assert.equal(typeof wire,'string'); native.push(decodePluginValue(new TextEncoder().encode(wire))); }, onActivity: () => activities.push(1), maxMessageBytes: 4096 });
  const event = (data, extra={}) => ({ source:window, origin:'null', data, ports:[], ...extra });
  return { bridge, frame, window, sent, native, activities, event };
}

test('opaque origin alone is not authority: sibling and nested WindowProxy messages are ignored', async () => {
  const r=rig();
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {source:{}}));
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {source:{parent:r.window}}));
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {origin:'https://other.invalid'}));
  assert.equal(r.native.length,0);
});
test('untrusted tier accepts only opaque origin even if a message claims privileged authority', async () => {
  const r=rig();
  await r.bridge.receive(r.event({type:'sandbox-ready',tier:'privileged'}, {origin:'https://plugin.invalid'}));
  assert.equal(r.native.length,0);
  await r.bridge.receive(r.event({type:'sandbox-ready'}));
  assert.equal(r.native.length,1);
});
test('host-selected privileged tier requires exact trusted origin and frame identity', async () => {
  const r=rig({tier:'privileged',hostOrigin:'https://plugin.invalid'});
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {origin:'null'}));
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {origin:'https://other.invalid'}));
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {origin:'https://plugin.invalid',source:{}}));
  assert.equal(r.native.length,0);
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {origin:'https://plugin.invalid'}));
  assert.equal(r.native.length,1);
});
test('only the first authenticated ready owns an activity MessagePort and teardown closes it', async () => {
  const r=rig(); const first = { onmessage:null, close(){ this.closed=true; } }, second = { onmessage:null, close(){ this.closed=true; } };
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {ports:[first]}));
  await r.bridge.receive(r.event({type:'sandbox-ready'}, {ports:[second]}));
  assert.equal(typeof first.onmessage,'function'); assert.equal(second.onmessage,null);
  first.onmessage({data:1}); assert.equal(r.activities.length,1);
  r.bridge.dispose(); assert.equal(first.closed,true); first.onmessage?.({data:1}); assert.equal(r.activities.length,1);
});
test('replaced iframe, navigation and disposal reject queued messages and sends', async () => {
  const r=rig(); const old=r.window;
  await r.bridge.receive(r.event({type:'sandbox-ready'}));
  r.frame.contentWindow = {};
  await r.bridge.receive(r.event({type:'api-request',id:'x',method:'storage.get',args:[]},{source:old}));
  r.bridge.dispose(); await r.bridge.receive(r.event({type:'sandbox-ready'}));
  await assert.rejects(() => r.bridge.send({type:'theme-change',theme:{dark:false,fontFamily:'x',vars:{}}}));
  assert.equal(r.native.filter(x => x.type === 'api-request').length,0);
});
const init = {type:'init',payload:{mode:'background',pluginId:'fixture',tier:'untrusted',manifest:{id:'fixture',version:'1',permissions:[],settings:{}},code:'',locale:'en'}};
async function readyAndInit(r) { await r.bridge.receive(r.event({type:'sandbox-ready'})); await r.bridge.send(init); }

test('API requests are rejected before init and accepted during activation', async () => {
  const r=rig(); await r.bridge.receive(r.event({type:'sandbox-ready'}));
  await r.bridge.receive(r.event({type:'api-request',id:'early',method:'storage.get',args:[]}));
  assert.equal(r.native.some(x => x.id === 'early'),false);
  await r.bridge.send(init);
  await r.bridge.receive(r.event({type:'api-request',id:'active',method:'storage.get',args:[]}));
  assert.equal(r.native.some(x => x.id === 'active'),true);
});
const inbound = [
  {type:'sandbox-ready'}, {type:'init-done',hooks:[],slots:[],shortcuts:[]}, {type:'init-error',error:'failed'},
  {type:'api-request',id:'a',method:'storage.get',args:[]}, {type:'callback-invoke',id:'b',callbackId:'c',args:[]},
  {type:'hook-result',id:'d',ok:true,result:null}, {type:'theme-applied',dark:false}, {type:'slot-resize',height:10},
  {type:'frame-escape'}, {type:'slot-should-show-result',id:'e',show:true},
  {type:'floating-dropdown-open',id:'f',rect:{left:0,top:0,bottom:1,width:1,height:1},options:[],selectedValues:[],highlightedIndex:0},
  {type:'floating-dropdown-update',id:'f'}, {type:'floating-dropdown-close',id:'f'},
];
for (const message of inbound) test(`source inbound variant ${message.type} reaches the authenticated bridge`, async () => {
  const r=rig(); if (message.type !== 'sandbox-ready') await readyAndInit(r);
  await r.bridge.receive(r.event(message)); assert.ok(r.native.some(x => x.type === message.type));
});
const theme={dark:false,fontFamily:'sans-serif',vars:{}}, features={externalSenderLabel:false,externalRecipientWarning:false,noThirdPartyTraffic:false};
const outbound = [
 init,
 {type:'api-response',id:'a',ok:true,result:null}, {type:'callback-response',id:'b',ok:true,result:null},
 {type:'hook-invoke',id:'c',hookName:'email:before-send',args:[]}, {type:'locale-change',locale:'en'},
 {type:'theme-change',theme}, {type:'settings-change',settings:{}}, {type:'host-change',features}, {type:'props-update',props:{}},
 {type:'slot-should-show',id:'d',slot:'reader-header',context:{}}, {type:'stream-chunk',streamId:'s',chunk:new Uint8Array([1,2]).buffer},
 {type:'stream-end',streamId:'s'}, {type:'stream-progress',streamId:'s',loaded:1,total:2},
 {type:'floating-dropdown-select',id:'f',option:{value:'a',label:'A'}}, {type:'floating-dropdown-highlight',id:'f',index:0}, {type:'floating-dropdown-dismiss',id:'f'},
];
for (const message of outbound) test(`source outbound variant ${message.type} targets the bound frame`, async () => {
  const r=rig(); if (message.type === 'init') await r.bridge.receive(r.event({type:'sandbox-ready'})); else await readyAndInit(r); await r.bridge.send(message);
  assert.equal(r.sent.at(-1)[0].type,message.type); assert.equal(r.sent.at(-1)[1],'*');
  if (message.type === 'stream-chunk') assert.deepEqual([...new Uint8Array(r.sent.at(-1)[0].chunk)],[1,2]);
});
test('unknown types and oversized clone graphs fail closed without forwarding', async () => {
  const r=rig(); await readyAndInit(r);
  await r.bridge.receive(r.event({type:'not-a-real-message',id:'x'}));
  await r.bridge.receive(r.event({type:'api-request',id:'x',method:'storage.get',args:['x'.repeat(20000)]}));
  assert.equal(r.native.filter(x => x.type === 'api-request' || x.type === 'not-a-real-message').length,0);
});

test('pending init is unique and privileged sends target only the selected origin', async () => {
  const r=rig({tier:'privileged'});
  await r.bridge.receive(r.event({type:'sandbox-ready'},{origin:'https://plugin.invalid'}));
  const first=r.bridge.send(init);
  await assert.rejects(() => r.bridge.send(init));
  await first;
  assert.equal(r.sent.at(-1)[1],'https://plugin.invalid');
});
test('async binary snapshots are dropped when the frame is replaced before forwarding', async () => {
  const r=rig(); await readyAndInit(r);
  const request=r.bridge.receive(r.event({type:'api-request',id:'stale',method:'upload',args:[new Blob(['payload'])]}));
  r.frame.contentWindow={}; await request;
  assert.equal(r.native.some(x => x.id === 'stale'),false);
});
test('activity is throttled and invalid multiple ports are never installed', async () => {
  const r=rig(); const a={onmessage:null,close(){}}, b={onmessage:null,close(){}};
  await r.bridge.receive(r.event({type:'sandbox-ready'},{ports:[a,b]}));
  assert.equal(r.native.length,0);
  await r.bridge.receive(r.event({type:'sandbox-ready'},{ports:[a]}));
  a.onmessage({}); a.onmessage({}); assert.equal(r.activities.length,1);
});
