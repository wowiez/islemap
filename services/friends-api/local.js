import { createServer } from 'node:http';
import { handler } from './lib/handler.js';

const routes = new Map(['account', 'friends', 'presence', 'health'].map(route => ['/api/' + route, handler(route)]));
createServer(async (req, res) => {
  res.status = value => { res.statusCode = value; return res; };
  res.json = value => res.end(JSON.stringify(value));
  const endpoint = routes.get(new URL(req.url, 'http://localhost').pathname);
  if (!endpoint) return res.status(404).json({ error: 'Endpoint không tồn tại.' });
  const chunks = []; let length = 0;
  for await (const chunk of req) {
    length += chunk.length;
    if (length > 16384) return res.status(413).json({ error: 'Payload quá lớn.' });
    chunks.push(chunk);
  }
  req.body = Buffer.concat(chunks).toString('utf8') || undefined;
  await endpoint(req, res);
}).listen(3000, '127.0.0.1', () => console.log('Local friends API: http://127.0.0.1:3000 (set database environment variables first)'));
