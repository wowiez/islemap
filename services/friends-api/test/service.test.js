import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync, sign } from 'node:crypto';
import { execute } from '../lib/service.js';
import { signatureText, visiblePresence, hash } from '../lib/rules.js';
import { AUTH_SCRIPT } from '../lib/ephemeral.js';
import { ensureUniqueNames } from '../lib/names.js';

const now = 1750000000000;
function match(doc, query) {
  return Object.entries(query).every(([key, value]) => {
    const actual = doc[key];
    if (value && typeof value === 'object' && '$in' in value) return value.$in.includes(actual);
    if (value && typeof value === 'object' && '$exists' in value) return (actual !== undefined) === value.$exists;
    return Array.isArray(actual) ? actual.includes(value) : actual === value;
  });
}
class Collection {
  docs = [];
  async createIndex() { return 'test-index'; }
  checkName(doc) { if (typeof doc.nameKey === 'string' && this.docs.some(item => item._id !== doc._id && item.nameKey === doc.nameKey)) throw Object.assign(new Error(), { code: 11000 }); }
  async insertOne(doc) { if (this.docs.some(item => item._id === doc._id)) throw Object.assign(new Error(), { code: 11000 }); this.checkName(doc); this.docs.push(structuredClone(doc)); }
  async findOne(query) { return structuredClone(this.docs.find(doc => match(doc, query)) ?? null); }
  find(query) { let maximum = Infinity; return { limit(n) { maximum = n; return this; }, sort() { return this; }, toArray: async () => structuredClone(this.docs.filter(doc => match(doc, query)).slice(0, maximum)) }; }
  async updateOne(query, change) { const doc = this.docs.find(doc => match(doc, query)); if (doc) { this.checkName({ ...doc, ...change.$set }); Object.assign(doc, change.$set); } return { matchedCount: doc ? 1 : 0 }; }
  async findOneAndUpdate(query, change) { const doc = this.docs.find(doc => match(doc, query)); if (!doc) return null; Object.assign(doc, change.$set); return structuredClone(doc); }
  async deleteOne(query) { const index = this.docs.findIndex(doc => match(doc, query)); if (index < 0) return { deletedCount: 0 }; this.docs.splice(index, 1); return { deletedCount: 1 }; }
}
class Redis {
  data = new Map();
  expiry = new Map();
  clock = now;
  reads = [];
  sweep() { for (const [key, at] of this.expiry) if (at <= this.clock) { this.data.delete(key); this.expiry.delete(key); } }
  async set(key, value, options) { this.sweep(); if (options?.NX && this.data.has(key)) return null; this.data.set(key, value); if (options?.EX) this.expiry.set(key, this.clock + options.EX * 1000); return 'OK'; }
  async del(key) { this.data.delete(key); this.expiry.delete(key); }
  async mGet(keys) { this.sweep(); this.reads.push(keys); return keys.map(key => this.data.get(key) ?? null); }
  async eval(script, options) {
    this.sweep();
    if (script === AUTH_SCRIPT) {
      const [nonces, rate, legacy] = options.keys;
      const [at, nonce] = options.arguments;
      const count = Number(this.data.get(rate) || 0) + 1;
      this.data.set(rate, count); if(count === 1) this.expiry.set(rate, this.clock + 60000);
      if(count > 180) return -1;
      const values = this.data.get(nonces) || new Map();
      for(const [key, time] of values) if(time <= Number(at) - 120000) values.delete(key);
      if(this.data.has(legacy) || values.has(nonce)) return 0;
      values.set(nonce, Number(at)); this.data.set(nonces, values); this.expiry.set(nonces, this.clock + 120000);
      return 1;
    }
    const key = options.keys[0]; const next = Number(this.data.get(key) || 0) + 1; this.data.set(key, next);
    if(next === 1) this.expiry.set(key, this.clock + Number(options.arguments[0]) * 1000);
    return next;
  }
}

async function fixture() {
  const store = { users: new Collection(), links: new Collection(), redis: new Redis() };
  async function account(name, num) {
    const keys = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
    const deviceId = String(num).padStart(32, '0');
    const body = { action: 'create', name, deviceId, publicKey: keys.publicKey.export({ type: 'spki', format: 'pem' }).toString() };
    const user = await execute('account', { method: 'POST', url: '/api/account', headers: {} }, JSON.stringify(body), store, now);
    let serial = 0;
    function request(route, body, nonce) {
      const raw = JSON.stringify(body);
      nonce ||= String(++serial).padStart(32, '0');
      const timestamp = String(now);
      const headers = { 'x-ilm-user': user.userId, 'x-ilm-device': deviceId, 'x-ilm-time': timestamp, 'x-ilm-nonce': nonce, 'x-ilm-signature': sign('sha256', Buffer.from(signatureText('POST', '/api/' + route, timestamp, nonce, raw)), keys.privateKey).toString('base64') };
      return { req: { method: 'POST', url: '/api/' + route, headers }, raw };
    }
    return { ...user, deviceId, request, call: (route, body) => { const { req, raw } = request(route, body); return execute(route, req, raw, store, now); } };
  }
  const a = await account('Alpha', 1), b = await account('Beta', 2), c = await account('Gamma', 3);
  return { store, a, b, c };
}
const presence = (server = 'sbtc', sharing = true) => ({ sharing, context: { server, map: 'gateway' }, position: { x: 0.4, y: 0.6 }, species: 'Pteranodon' });

test('500 friends use one bulk read; hidden, stale and other-server coordinates never leak', async () => {
  const { a, store } = await fixture();
  for (let i = 0; i < 500; i++) {
    const id = (1000 + i).toString(16).padStart(32, '0');
    store.users.docs.push({ _id: id, name: `Friend ${i}` });
    store.links.docs.push({ _id: `${a.userId}:${id}`, members: [a.userId, id], state: 'accepted' });
    if(i % 5 !== 4) await store.redis.set(`ilm:presence:${id}`, JSON.stringify({
      at: i % 5 === 3 ? now - 10000 : now, sharing: i % 5 !== 1,
      server: i % 5 === 2 ? 'other' : 'sbtc', map: 'gateway', position: { x: .5, y: .5 }, name: `Friend ${i}`, species: 'Ptera'
    }), { EX: 10 });
  }
  const start = performance.now();
  const result = await a.call('presence', presence());
  assert.equal(result.friends.length, 500);
  assert.equal(result.friends.filter(friend => friend.position).length, 100);
  assert.equal(result.friends.filter(friend => friend.online).length, 300);
  assert.equal(store.redis.reads.length, 1);
  assert.equal(store.redis.reads[0].length, 500);
  assert.equal((await a.call('friends', { action: 'list' })).friends.length, 500);
  console.log(`500-friend fixture: ${(performance.now() - start).toFixed(1)} ms (in-memory, not production capacity)`);
  store.redis.clock += 10000; store.redis.sweep();
  assert.equal([...store.redis.data.keys()].filter(key => key.startsWith('ilm:presence:')).length, 0);
  assert.equal(store.links.docs.length, 500);
});

test('Redis auth state is bounded per user, expires, rejects replays and preserves legacy replay protection', async () => {
  const { a, store } = await fixture();
  for(let i = 0; i < 180; i++) await a.call('presence', presence());
  assert.equal([...store.redis.data.keys()].filter(key => key.startsWith('ilm:nonces:')).length, 1);
  assert.equal(store.redis.data.get(`ilm:nonces:${a.userId}`).size, 180);
  assert.equal([...store.redis.data.keys()].filter(key => key.startsWith('ilm:nonce:')).length, 0);
  await assert.rejects(a.call('presence', presence()), error => error.status === 429);
  store.redis.clock += 120000; store.redis.sweep();
  assert.equal([...store.redis.data.keys()].filter(key => !key.startsWith('ilm:rate:registration:')).length, 0);
  const nonce = 'f'.repeat(32);
  await store.redis.set(`ilm:nonce:${a.userId}:${nonce}`, '1', { EX: 120 });
  const replay = a.request('presence', presence(), nonce);
  await assert.rejects(execute('presence', replay.req, replay.raw, store, now), error => error.status === 401);
  store.redis.clock += 3600000; store.redis.sweep();
  assert.equal(store.redis.data.size, 0);
});

test('Pending invitations never expose positions, only recipient can accept', async () => {
  const { a, b, c } = await fixture();
  await a.call('friends', { action: 'request', code: b.friendCode });
  await b.call('presence', presence());
  assert.deepEqual((await a.call('presence', presence())).friends, []);
  await assert.rejects(a.call('friends', { action: 'accept', userId: b.userId }), error => error.status === 404);
  await assert.rejects(c.call('friends', { action: 'accept', userId: a.userId }), error => error.status === 404);
  await b.call('friends', { action: 'accept', userId: a.userId });
  assert.equal((await a.call('presence', presence())).friends[0].position.x, 0.4);
});
test('Reject, cancel and remove permissions are enforced and friendship persists without presence', async () => {
  const { a, b, store } = await fixture();
  await a.call('friends', { action: 'request', code: b.friendCode });
  await assert.rejects(a.call('friends', { action: 'reject', userId: b.userId }));
  await b.call('friends', { action: 'reject', userId: a.userId });
  await a.call('friends', { action: 'request', code: b.friendCode });
  await a.call('friends', { action: 'cancel', userId: b.userId });
  await a.call('friends', { action: 'request', code: b.friendCode });
  await b.call('friends', { action: 'accept', userId: a.userId });
  store.redis.data.clear();
  assert.equal((await a.call('friends', { action: 'list' })).friends[0].state, 'accepted');
  await a.call('friends', { action: 'remove', userId: b.userId });
  assert.deepEqual((await b.call('friends', { action: 'list' })).friends, []);
});
test('Sharing defaults off, different server and unknown context never return coordinates', async () => {
  const { a, b, store } = await fixture();
  await a.call('friends', { action: 'request', code: b.friendCode });
  await b.call('friends', { action: 'accept', userId: a.userId });
  await b.call('presence', { ...presence(), sharing: undefined });
  assert.equal(JSON.parse(store.redis.data.get(`ilm:presence:${b.userId}`)).position, null);
  assert.equal((await a.call('presence', presence())).friends[0].position, null);
  await b.call('presence', presence('other'));
  const other = (await a.call('presence', presence())).friends[0];
  assert.equal(other.online, true); assert.equal(other.sameServer, false); assert.equal(other.position, null);
  await b.call('presence', presence());
  assert.equal((await a.call('presence', { context: null })).friends[0].position, null);
  await b.call('presence', presence('sbtc', false));
  assert.equal((await a.call('presence', presence())).friends[0].position, null);
});
test('Expiry and malformed coordinates are rejected', () => {
  const item = { sharing: true, position: { x: 0.5, y: 0.5 }, server: 'sbtc', map: 'gateway', at: now };
  assert.equal(visiblePresence(item, { server: 'sbtc', map: 'gateway' }, now + 9999), true);
  assert.equal(visiblePresence(item, { server: 'sbtc', map: 'gateway' }, now + 10000), false);
  assert.equal(visiblePresence(item, { server: 'sbtc', map: 'other' }, now), false);
});
test('Replay and changed payload or route fail signature verification', async () => {
  const { a, store } = await fixture();
  const { req, raw } = a.request('account', { action: 'me' });
  await execute('account', req, raw, store, now);
  await assert.rejects(execute('account', req, raw, store, now), error => error.status === 401);
  const changed = a.request('account', { action: 'me' });
  await assert.rejects(execute('account', changed.req, JSON.stringify({ action: 'rename', name: 'Hacked' }), store, now), error => error.status === 401);
  changed.req.url = '/api/friends';
  await assert.rejects(execute('friends', changed.req, changed.raw, store, now), error => error.status === 401);
});
test('Recovery preserves account and friendships, rotates recovery and revokes old devices', async () => {
  const { a, b, store } = await fixture();
  await a.call('friends', { action: 'request', code: b.friendCode });
  await b.call('friends', { action: 'accept', userId: a.userId });
  const keys = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const body = JSON.stringify({ action: 'recover', recoveryCode: a.recoveryCode, deviceId: 'f'.repeat(32), publicKey: keys.publicKey.export({ type: 'spki', format: 'pem' }).toString() });
  const req = { method: 'POST', url: '/api/account', headers: {} };
  const restored = await execute('account', req, body, store, now);
  assert.equal(restored.userId, a.userId); assert.equal(restored.friendCode, a.friendCode);
  assert.notEqual(restored.recoveryCode, a.recoveryCode);
  assert.equal(store.links.docs[0].state, 'accepted');
  assert.equal(store.users.docs.find(user => user._id === a.userId).recoveryHash, hash(restored.recoveryCode.split('.')[2]));
  await assert.rejects(a.call('account', { action: 'me' }), error => error.status === 401);
  await assert.rejects(execute('account', req, body, store, now), error => error.status === 401);
});
test('Rename keeps stable identity and public code; secret keys never appear in public profiles', async () => {
  const { a } = await fixture();
  const profile = await a.call('account', { action: 'rename', name: 'New Alpha' });
  assert.equal(profile.userId, a.userId); assert.equal(profile.friendCode, a.friendCode);
  assert.equal(profile.name, 'New Alpha'); assert.equal(profile.recoveryHash, undefined); assert.equal(profile.devices, undefined);
});
test('Unique names reject case variants on creation and rename without changing existing account', async () => {
  const { a, b, store } = await fixture();
  await assert.rejects(a.call('account', { action: 'rename', name: '  bETA ' }), error => error.status === 409);
  assert.equal((await a.call('account', { action: 'me' })).name, 'Alpha');
  const keys = generateKeyPairSync('ec', { namedCurve: 'prime256v1' });
  const raw = JSON.stringify({ action: 'create', name: ' aLPHa ', deviceId: 'f'.repeat(32), publicKey: keys.publicKey.export({ type: 'spki', format: 'pem' }).toString() });
  await assert.rejects(execute('account', { method: 'POST', url: '/api/account', headers: {} }, raw, store, now), error => error.status === 409);
  await b.call('account', { action: 'rename', name: 'Kháng' });
  await assert.rejects(a.call('account', { action: 'rename', name: 'KHA\u0301NG' }), error => error.status === 409);
});
test('Invitation by name is case insensitive, and name/code lookups remain unambiguous', async () => {
  const { a, b, c } = await fixture();
  await a.call('friends', { action: 'request', name: ' bETA ' });
  assert.equal((await b.call('friends', { action: 'list' })).friends[0].state, 'incoming');
  await b.call('friends', { action: 'reject', userId: a.userId });
  await c.call('account', { action: 'rename', name: b.friendCode });
  await a.call('friends', { action: 'request', name: b.friendCode });
  assert.equal((await c.call('friends', { action: 'list' })).friends[0].state, 'incoming');
  assert.equal((await b.call('friends', { action: 'list' })).friends.length, 0);
  await c.call('friends', { action: 'reject', userId: a.userId });
  await a.call('friends', { action: 'request', code: b.friendCode });
  assert.equal((await b.call('friends', { action: 'list' })).friends[0].state, 'incoming');
});
test('Legacy duplicate migration is idempotent and preserves IDs and friend codes', async () => {
  const users = new Collection();
  await users.insertOne({ _id: '1'.repeat(32), name: 'Alpha', friendCode: 'code-one' });
  await users.insertOne({ _id: '2'.repeat(32), name: 'ALPHA', friendCode: 'code-two' });
  await ensureUniqueNames(users);
  assert.equal(users.docs[0].name, 'Alpha');
  assert.equal(users.docs[1].name, 'ALPHA-22222222');
  assert.equal(users.docs[1].friendCode, 'code-two');
  const migrated = structuredClone(users.docs);
  await ensureUniqueNames(users);
  assert.deepEqual(users.docs, migrated);
});
