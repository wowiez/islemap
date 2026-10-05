import test from 'node:test';
import assert from 'node:assert/strict';
import { forward } from '../lib/bridge.js';

test('Bridge preserves signed payload and auth headers, strips untrusted forwarding headers', async () => {
  const req = { method: 'POST', body: { payload: '{ "action": "list" }' }, headers: { 'x-ilm-user': 'user', 'x-ilm-signature': 'signature', 'x-forwarded-for': 'spoof', authorization: 'secret' } };
  const res = { status(code) { this.code = code; return this; }, send(body) { this.body = body; } };
  let calls=0;
  await forward('friends',req,res,'https://southtampanailsfl.com/api/islemap/', async (url, options) => {
    calls++; assert.equal(url.href,'https://southtampanailsfl.com/api/islemap/friends');
    assert.equal(JSON.parse(options.body).payload,req.body.payload);
    assert.equal(options.headers['x-ilm-signature'],'signature');
    assert.equal(options.headers['x-forwarded-for'],undefined); assert.equal(options.headers.authorization,undefined);
    assert.equal(options.redirect,'error'); return new Response('{"error":"denied"}',{status:401});
  });
  assert.equal(calls,1); assert.equal(res.code,401); assert.equal(res.body,'{"error":"denied"}');
});
test('Bridge rejects unexpected origins without forwarding a request', async () => {
  for (const origin of ['http://southtampanailsfl.com/api/islemap/','https://example.com/api/islemap/','https://southtampanailsfl.com/'])
    await assert.rejects(forward('account',{}, {},origin,()=>{throw new Error('must not run');}),/Invalid migration origin/);
});
