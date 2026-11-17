(function(){
var cur=null,nxt=null,seq=0;
function post(m){try{window.ipc.postMessage(m)}catch(e){}}
// the view owns the OS keyboard focus once it was clicked: pass the app's shortcuts back to the native side
function kd(e){try{if(e.defaultPrevented||e.isComposing)return;var t=e.target,n=t&&t.tagName;
if(t&&(t.isContentEditable||n==='INPUT'||n==='TEXTAREA'||n==='SELECT'))return;
var k=e.key,c=e.ctrlKey||e.metaKey,a=e.altKey,fw=false;
if(c&&!a){fw=['k','e','f','m'].indexOf(k.toLowerCase())>=0}
else if(!a){fw=k.length===1&&k!==' '||k==='Escape'||k==='Delete'||k==='F5'||k==='F9'}
if(!fw)return;e.preventDefault();post('key:'+(c?'c':'')+(e.shiftKey?'s':'')+(a?'a':'')+'|'+(k.length===1&&c?k.toLowerCase():k))}catch(x){}}
document.addEventListener('keydown',kd,true);
function fix(d){try{var a=d.querySelectorAll('a,area,form');for(var i=0;i<a.length;i++)a[i].setAttribute('target','_blank')}catch(e){}
try{if(!d.__bbd){d.__bbd=1;var t=0,p=function(){if(t)return;t=1;requestAnimationFrame(function(){t=0;post('dirty')})};
d.addEventListener('keydown',kd,true);d.addEventListener('scroll',p,true);d.addEventListener('load',p,true);d.addEventListener('selectionchange',p);d.defaultView.addEventListener('resize',p)}}catch(e){}}
window.__bb={
show:function(s,html,bg,sb){seq=s;if(nxt){nxt.remove();nxt=null}
var f=document.createElement('iframe');if(sb!==null)f.setAttribute('sandbox',sb);f.setAttribute('title','Message');
f.style.cssText='position:absolute;left:0;top:0;width:100%;height:100%;border:0;margin:0;visibility:hidden;background:'+bg;
f._bg=bg;nxt=f;var done=false;
function ready(){if(done||seq!==s)return;done=true;post('ready:'+s)}
f.addEventListener('load',function(){try{fix(f.contentDocument)}catch(e){}ready()});f.srcdoc=html;document.body.appendChild(f);
(function poll(){if(done||seq!==s)return;var d=null;try{d=f.contentDocument}catch(e){}
if(d&&d.URL==='about:srcdoc'&&d.readyState!=='loading'&&d.body&&!d.documentElement.classList.contains('barbican-dark-pending')){fix(d);requestAnimationFrame(ready);return}
requestAnimationFrame(poll)})();},
reveal:function(s){if(s!==seq||!nxt)return;var f=nxt;nxt=null;document.documentElement.style.background=f._bg;
f.style.visibility='visible';if(cur)cur.remove();cur=f},
cancel:function(s){seq=s;if(nxt){nxt.remove();nxt=null}},
dim:function(on){if(cur)cur.style.opacity=on?'0.4':''},
clear:function(bg){seq++;if(nxt){nxt.remove();nxt=null}if(cur){cur.remove();cur=null}document.documentElement.style.background=bg},
print:function(){if(cur)cur.contentWindow.print()}
};post('shell')})();
