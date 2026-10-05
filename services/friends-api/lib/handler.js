import { storage } from './storage.js';
import { execute } from './service.js';
import { ApiError } from './rules.js';
import { forward } from './bridge.js';

export function handler(route) {
  return async (req, res) => {
    res.setHeader('Cache-Control', 'private, no-store');
    res.setHeader('Content-Type', 'application/json; charset=utf-8');
    try {
      if (process.env.ISLEMAP_MAINTENANCE === '1') return res.status(503).json({ error: 'Đang chuyển dữ liệu, vui lòng thử lại sau.' });
      if (process.env.ISLEMAP_ORIGIN) return await forward(route, req, res, process.env.ISLEMAP_ORIGIN);
      if (route === 'health') {
        const store = await storage();
        await Promise.all([store.redis.ping(), store.users.findOne({}, { projection: { _id: 1 } })]);
        return res.status(200).json({ ok: true, service: 'IsleLiveMap Friends', version: 1 });
      }
      // Envelope retains the exact signed JSON bytes even if Vercel parses req.body.
      let envelope;
      try { envelope = typeof req.body === 'string' ? JSON.parse(req.body) : req.body; }
      catch { throw new ApiError(400, 'JSON không hợp lệ.'); }
      if (typeof envelope?.payload !== 'string' || Buffer.byteLength(envelope.payload) > 8192) throw new ApiError(400, 'Payload không hợp lệ.');
      const result = await execute(route, req, envelope.payload, await storage());
      res.status(200).json(result);
    } catch (error) {
      const status = error instanceof ApiError ? error.status : 503;
      // Never log database exceptions: connection errors can contain credentials.
      res.status(status).json({ error: status === 503 ? 'API tạm thời không khả dụng.' : error.message });
    }
  };
}
