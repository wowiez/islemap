import { randomBytes } from 'node:crypto';
import { AUTH_SCRIPT } from './ephemeral.js';
import { ApiError, TTL, requireValue, hash, name, nameKey, publicKey, identifier, pairId, signatureText, validSignature, context, position, visiblePresence } from './rules.js';

const secret = () => randomBytes(32).toString('base64url');
const id = () => randomBytes(16).toString('hex');
const profile = user => ({ userId: user._id, name: user.name, friendCode: user.friendCode });
async function limit(redis, key, maximum, seconds) {
  // Atomic increment + expiry, including failed requests.
  const count = await redis.eval("local n=redis.call('INCR',KEYS[1]); if n==1 then redis.call('EXPIRE',KEYS[1],ARGV[1]) end; return n", { keys: [`ilm:rate:${key}`], arguments: [String(seconds)] });
  requireValue(count <= maximum, 'Quá nhiều yêu cầu, thử lại sau.', 429);
}
async function authenticate(req, raw, store, now) {
  const userId = req.headers['x-ilm-user'];
  const deviceId = req.headers['x-ilm-device'];
  const timestamp = req.headers['x-ilm-time'];
  const nonce = req.headers['x-ilm-nonce'];
  requireValue(identifier(userId) && identifier(deviceId) && identifier(nonce) && typeof timestamp === 'string' && /^\d{10,13}$/.test(timestamp) && Math.abs(now - Number(timestamp)) < 60000, 'Phiên xác thực không hợp lệ.', 401);
  const user = await store.users.findOne({ _id: userId });
  const device = user?.devices?.find(item => item.id === deviceId);
  requireValue(device && validSignature(device.key, signatureText(req.method, new URL(req.url, 'https://local').pathname, timestamp, nonce, raw), req.headers['x-ilm-signature']), 'Thiết bị chưa được xác thực.', 401);
  const authorized = await store.redis.eval(AUTH_SCRIPT, {
    keys: [`ilm:nonces:${userId}`, `ilm:rate:user:${userId}`, `ilm:nonce:${userId}:${nonce}`],
    arguments: [String(now), nonce]
  });
  requireValue(authorized !== -1, 'Quá nhiều yêu cầu, thử lại sau.', 429);
  requireValue(authorized === 1, 'Yêu cầu đã được sử dụng.', 401);
  return user;
}

export async function execute(route, req, raw, store, now = Date.now()) {
  requireValue(req.method === 'POST', 'Chỉ hỗ trợ POST.', 405);
  let body;
  try { body = JSON.parse(raw); } catch { throw new ApiError(400, 'JSON không hợp lệ.'); }
  requireValue(body && typeof body === 'object' && !Array.isArray(body), 'Dữ liệu không hợp lệ.');
  if (route === 'account' && ['create', 'recover'].includes(body.action)) {
    const ip = req.headers['x-vercel-forwarded-for'] || req.headers['x-forwarded-for'] || req.socket?.remoteAddress || 'unknown';
    await limit(store.redis, `registration:${hash(String(ip))}`, 12, 3600);
    requireValue(identifier(body.deviceId), 'Thiết bị không hợp lệ.');
    const key = publicKey(body.publicKey);
    const recovery = secret();
    if (body.action === 'create') {
      const user = { _id: id(), name: name(body.name), nameKey: nameKey(body.name), friendCode: randomBytes(6).toString('hex').toUpperCase(), recoveryHash: hash(recovery), devices: [{ id: body.deviceId, key }], createdAt: new Date(now) };
      try { await store.users.insertOne(user); }
      catch (error) { if (error.code === 11000) throw new ApiError(409, 'Tên đã được sử dụng. Hãy chọn tên khác.'); throw error; }
      return { ...profile(user), recoveryCode: `ILM1.${user._id}.${recovery}` };
    }
    const parts = typeof body.recoveryCode === 'string' ? body.recoveryCode.split('.') : [];
    requireValue(parts.length === 3 && parts[0] === 'ILM1' && identifier(parts[1]) && /^[A-Za-z0-9_-]{43}$/.test(parts[2]), 'Mã khôi phục không hợp lệ.');
    // One-use recovery, revokes old devices and resets live sharing immediately.
    const user = await store.users.findOneAndUpdate({ _id: parts[1], recoveryHash: hash(parts[2]) }, { $set: { recoveryHash: hash(recovery), devices: [{ id: body.deviceId, key }] } }, { returnDocument: 'after' });
    requireValue(user, 'Mã khôi phục sai hoặc đã được dùng.', 401);
    await store.redis.del(`ilm:presence:${user._id}`);
    return { ...profile(user), recoveryCode: `ILM1.${user._id}.${recovery}` };
  }
  const user = await authenticate(req, raw, store, now);
  if (route === 'account') {
    if (body.action === 'rename') {
      user.name = name(body.name);
      try { await store.users.updateOne({ _id: user._id }, { $set: { name: user.name, nameKey: nameKey(user.name) } }); }
      catch (error) { if (error.code === 11000) throw new ApiError(409, 'Tên đã được sử dụng. Hãy chọn tên khác.'); throw error; }
    } else if (body.action === 'recovery') {
      const recovery = secret();
      await store.users.updateOne({ _id: user._id }, { $set: { recoveryHash: hash(recovery) } });
      return { ...profile(user), recoveryCode: `ILM1.${user._id}.${recovery}` };
    } else requireValue(body.action === 'me', 'Thao tác không hợp lệ.');
    return profile(user);
  }
  if (route === 'friends') {
    if (body.action === 'request') {
      await limit(store.redis, `invitations:${user._id}`, 20, 3600);
      const byName = typeof body.name === 'string';
      requireValue(byName || (typeof body.code === 'string' && /^[A-Fa-f0-9]{12}$/.test(body.code.trim())), 'Tên hoặc mã kết bạn không hợp lệ.');
      const other = await store.users.findOne(byName ? { nameKey: nameKey(body.name) } : { friendCode: body.code.trim().toUpperCase() });
      requireValue(other && other._id !== user._id, 'Không tìm thấy người dùng hoặc bạn đang nhập mã của mình.');
      for (const member of [user._id, other._id]) {
        const existing = await store.links.find({ members: member }).limit(501).toArray();
        requireValue(existing.length < 500, 'Danh sách bạn bè/lời mời đã đầy.', 409);
      }
      const link = { _id: pairId(user._id, other._id), members: [user._id, other._id], from: user._id, to: other._id, state: 'pending', at: new Date(now) };
      try { await store.links.insertOne(link); } catch (error) {
        if (error.code === 11000) throw new ApiError(409, 'Đã có lời mời hoặc đã là bạn bè.');
        throw error;
      }
    } else if (['accept', 'reject', 'cancel', 'remove'].includes(body.action)) {
      requireValue(identifier(body.userId) && body.userId !== user._id, 'Người dùng không hợp lệ.');
      const filter = { _id: pairId(user._id, body.userId), members: user._id };
      if (['accept', 'reject'].includes(body.action)) Object.assign(filter, { to: user._id, state: 'pending' });
      if (body.action === 'cancel') Object.assign(filter, { from: user._id, state: 'pending' });
      if (body.action === 'remove') filter.state = 'accepted';
      const result = body.action === 'accept'
        ? await store.links.updateOne(filter, { $set: { state: 'accepted', acceptedAt: new Date(now) } })
        : await store.links.deleteOne(filter);
      requireValue((result.matchedCount ?? result.deletedCount) > 0, 'Lời mời hoặc bạn bè không còn tồn tại.', 404);
    } else requireValue(body.action === 'list', 'Thao tác không hợp lệ.');
    const links = await store.links.find({ members: user._id }).limit(501).toArray();
    requireValue(links.length <= 500, 'Danh sách vượt giới hạn.', 409);
    const ids = links.map(link => link.members.find(member => member !== user._id));
    const people = ids.length ? await store.users.find({ _id: { $in: ids } }, { projection: { name: 1 } }).toArray() : [];
    const names = new Map(people.map(person => [person._id, person.name]));
    return { ...profile(user), friends: links.map((link, i) => ({ userId: ids[i], name: names.get(ids[i]) || 'Người chơi', state: link.state === 'accepted' ? 'accepted' : link.to === user._id ? 'incoming' : 'outgoing' })) };
  }
  if (route === 'presence') {
    const ownContext = context(body.context);
    const ownPosition = position(body.position);
    const sharing = body.sharing === true && ownContext !== null && ownPosition !== null;
    const presence = { at: now, sharing, ...ownContext, position: sharing ? ownPosition : null, species: typeof body.species === 'string' ? body.species.slice(0, 64) : '', name: user.name };
    await store.redis.set(`ilm:presence:${user._id}`, JSON.stringify(presence), { EX: TTL });
    const links = await store.links.find({ members: user._id, state: 'accepted' }).limit(500).toArray();
    const ids = links.map(link => link.members.find(member => member !== user._id));
    const values = ids.length ? await store.redis.mGet(ids.map(other => `ilm:presence:${other}`)) : [];
    return { serverTime: now, friends: values.map((value, i) => {
      let item;
      try { item = JSON.parse(value); } catch { item = null; }
      const online = item && Number.isFinite(item.at) && now - item.at >= 0 && now - item.at < TTL * 1000;
      const visible = visiblePresence(item, ownContext, now);
      return { userId: ids[i], online: Boolean(online), name: online ? item.name : null, species: online ? item.species : null, position: visible ? item.position : null, at: online ? item.at : null, sameServer: Boolean(online && ownContext && item.server === ownContext.server && item.map === ownContext.map) };
    }) };
  }
  throw new ApiError(404, 'Endpoint không tồn tại.');
}
