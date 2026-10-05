import { createHash, createPublicKey, verify } from 'node:crypto';

export const TTL = 10;
export class ApiError extends Error {
  constructor(status, message) { super(message); this.status = status; }
}
export function requireValue(condition, message, status = 400) {
  if (!condition) throw new ApiError(status, message);
}
export const hash = value => createHash('sha256').update(value).digest('hex');
export function name(value) {
  requireValue(typeof value === 'string', 'Tên không hợp lệ.');
  const result = value.trim().normalize('NFC');
  requireValue(result.length >= 2 && result.length <= 32 && !/[\p{C}]/u.test(result), 'Tên cần 2–32 ký tự, không chứa ký tự điều khiển.');
  return result;
}
export const nameKey = value => name(value).toLowerCase();
export function publicKey(value) {
  requireValue(typeof value === 'string' && value.length < 1024, 'Khóa thiết bị không hợp lệ.');
  try {
    const key = createPublicKey(value);
    requireValue(key.asymmetricKeyType === 'ec' && key.asymmetricKeyDetails.namedCurve === 'prime256v1', 'Khóa thiết bị không hợp lệ.');
    return key.export({ type: 'spki', format: 'pem' }).toString();
  } catch { throw new ApiError(400, 'Khóa thiết bị không hợp lệ.'); }
}
export const identifier = value => typeof value === 'string' && /^[a-f0-9]{32}$/.test(value);
export const pairId = (a, b) => [a, b].sort().join(':');
export function signatureText(method, path, timestamp, nonce, body) {
  return `${method}\n${path}\n${timestamp}\n${nonce}\n${body}`;
}
export function validSignature(key, text, signature) {
  try { return verify('sha256', Buffer.from(text), key, Buffer.from(signature, 'base64')); } catch { return false; }
}
export function context(body) {
  if (!body || typeof body !== 'object' || typeof body.server !== 'string' || !body.server.trim() || body.server.length > 160 || body.map !== 'gateway') return null;
  return { server: body.server.trim().normalize('NFC').toLowerCase(), map: 'gateway' };
}
export function position(body) {
  if (!body || !Number.isFinite(body.x) || !Number.isFinite(body.y) || body.x < 0 || body.x > 1 || body.y < 0 || body.y > 1) return null;
  return { x: body.x, y: body.y, heading: Number.isFinite(body.heading) ? ((body.heading % 360) + 360) % 360 : null };
}
export function visiblePresence(presence, ownContext, now) {
  return Boolean(presence?.sharing && presence.position && ownContext && presence.server === ownContext.server && presence.map === ownContext.map && Number.isFinite(presence.at) && now >= presence.at && now - presence.at < TTL * 1000);
}
