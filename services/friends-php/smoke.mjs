import assert from 'node:assert/strict';
import { generateKeyPairSync, randomBytes, sign } from 'node:crypto';

// Run against an isolated QA database; creates synthetic accounts, never prints keys.
const http = process.argv[2]; const wsUrl = process.argv[3];
if (!http || !wsUrl) throw new Error('Specify QA HTTP base and WebSocket URL');
const deadline=setTimeout(()=>{ console.error('WebSocket smoke timed out'); process.exit(1); },60000);
const actors=[];
const body=(server='sbtc',sharing=true,x=.4)=>({sharing,context:{server,map:'gateway'},position:{x,y:.6,heading:30},species:'Ptera'});
async function request(a,route,data,legacy=false) {
  const payload=JSON.stringify(data); const path=legacy?'/api/'+route:new URL(route,http).pathname;
  const response=await fetch(new URL(route,http),{method:'POST',headers:{'content-type':'application/json',...(a?headers(a,path,payload):{})},body:JSON.stringify({payload})});
  const json=await response.json(); assert.equal(response.status,200,JSON.stringify(json)); return json;
}
function headers(a,path,payload) {
  const timestamp=String(Date.now()), nonce=randomBytes(16).toString('hex');
  return {'x-ilm-user':a.userId,'x-ilm-device':a.device,'x-ilm-time':timestamp,'x-ilm-nonce':nonce,'x-ilm-signature':sign('sha256',Buffer.from(`POST\n${path}\n${timestamp}\n${nonce}\n${payload}`),a.privateKey).toString('base64')};
}
async function actor(name) {
  const key=generateKeyPairSync('ec',{namedCurve:'prime256v1'}); const device=randomBytes(16).toString('hex');
  const profile=await request(null,'account',{action:'create',name,deviceId:device,publicKey:key.publicKey.export({type:'spki',format:'pem'})});
  const a={...profile,device,privateKey:key.privateKey,snapshots:[]}; actors.push(a); return a;
}
async function connect(a,data) {
  const socket=new WebSocket(wsUrl); a.socket=socket;
  await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',reject,{once:true});});
  socket.addEventListener('message',event=>{const m=JSON.parse(event.data); if(m.type==='snapshot')a.snapshots.push(m); if(m.type==='error')throw new Error('WebSocket rejected frame');});
  const payload=JSON.stringify(data); socket.send(JSON.stringify({type:'auth',payload,headers:headers(a,new URL(wsUrl).pathname,payload)}));
}
const delay=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function until(a,predicate) { for(let i=0;i<50;i++){ const last=a.snapshots.at(-1); if(last&&predicate(last))return last; await delay(100); } throw new Error('Expected pushed snapshot did not arrive'); }
try {
  const suffix=randomBytes(4).toString('hex'); const a=await actor('Socket A '+suffix),b=await actor('Socket B '+suffix);
  await connect(a,body()); await connect(b,body());
  await until(a,s=>s.friends.length===0);
  await request(a,'friends',{action:'request',name:b.name},true);
  assert.equal((await request(a,'presence',body(),true)).friends.length,0);
  await request(b,'friends',{action:'accept',userId:a.userId});
  await until(a,s=>s.friends[0]?.position?.x===.4);
  const packetOnly=body('udp:192.0.2.10:7777'); packetOnly.context.endpoint='udp:192.0.2.10:7777';
  const withWeb=body('[SEA/VN]-SDVN-#3-X3 Grow'); withWeb.context.endpoint=packetOnly.context.endpoint;
  a.socket.send(JSON.stringify({type:'presence',data:packetOnly}));
  b.socket.send(JSON.stringify({type:'presence',data:withWeb}));
  await until(a,s=>s.context?.endpoint===packetOnly.context.endpoint&&s.friends[0]?.sameServer&&s.friends[0]?.position!==null);
  b.socket.send(JSON.stringify({type:'presence',data:{...withWeb,context:{...withWeb.context,endpoint:'udp:192.0.2.10:7778'}}}));
  await until(a,s=>s.friends[0]?.sameServer===false&&s.friends[0]?.position===null);
  a.socket.send(JSON.stringify({type:'presence',data:body('sdvn3')}));
  b.socket.send(JSON.stringify({type:'presence',data:body('[SEA/VN]-SDVN-#3-X3')}));
  await until(a,s=>s.context?.server==='sdvn3'&&s.friends[0]?.sameServer===true&&s.friends[0]?.position!==null);
  await until(b,s=>s.context?.server==='[sea/vn]-sdvn-#3-x3'&&s.friends[0]?.sameServer===true);
  b.socket.send(JSON.stringify({type:'presence',data:body('SDVN #2')}));
  await until(a,s=>s.friends[0]?.sameServer===false&&s.friends[0]?.position===null);
  const renamed=await request(b,'account',{action:'rename',name:'Người Bạn '+suffix});
  assert.equal(renamed.userId,b.userId); assert.equal(renamed.friendCode,b.friendCode);
  await until(a,s=>s.friends[0]?.name==='Người Bạn '+suffix);
  a.socket.send(JSON.stringify({type:'presence',data:body()}));
  b.socket.send(JSON.stringify({type:'presence',data:body('other')}));
  await until(a,s=>s.friends[0]?.sameServer===false&&s.friends[0]?.position===null);
  b.socket.send(JSON.stringify({type:'presence',data:body('sbtc',false)}));
  await until(a,s=>s.friends[0]?.sameServer===true&&s.friends[0]?.position===null);
  b.socket.send(JSON.stringify({type:'presence',data:body('sbtc',true,.7)}));
  await until(a,s=>s.friends[0]?.position?.x===.7);
  await request(a,'friends',{action:'remove',userId:b.userId}); await until(a,s=>s.friends.length===0);
  await request(a,'friends',{action:'request',code:b.friendCode}); await request(b,'friends',{action:'accept',userId:a.userId});
  a.socket.send(JSON.stringify({type:'presence',data:body()})); b.socket.send(JSON.stringify({type:'presence',data:body()}));
  await until(a,s=>s.friends[0]?.position?.x===.4); await delay(11000);
  await until(a,s=>s.friends[0]?.online===false&&s.friends[0]?.position===null);
  b.socket.send(JSON.stringify({type:'presence',data:body()})); a.socket.send(JSON.stringify({type:'presence',data:body()}));
  await until(a,s=>s.friends[0]?.position?.x===.4); b.socket.close();
  await until(a,s=>s.friends[0]?.online===false);
  console.log('Live HTTP + WebSocket checks passed: SDVN aliases, distinct servers, Unicode rename push, legacy signatures, consent, sharing off, movement, unfriend, expiry, disconnect.');
} finally { for(const a of actors)a.socket?.close(); clearTimeout(deadline); }
