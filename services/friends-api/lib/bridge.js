// Only a configured deployment origin can receive signed requests. Never use a client-supplied URL.
export async function forward(route, req, res, origin, fetcher = fetch) {
  const base = new URL(origin);
  if (base.protocol !== 'https:' || base.hostname !== 'southtampanailsfl.com' || base.pathname !== '/api/islemap/') throw new Error('Invalid migration origin');
  const headers = { 'content-type': 'application/json' };
  for (const key of ['x-ilm-user', 'x-ilm-device', 'x-ilm-time', 'x-ilm-nonce', 'x-ilm-signature']) {
    const value = req.headers[key];
    if (typeof value === 'string') headers[key] = value;
  }
  const response = await fetcher(new URL(route, base), {
    method: req.method, headers, redirect: 'error', signal: AbortSignal.timeout(8000),
    ...(req.method === 'POST' ? { body: typeof req.body === 'string' ? req.body : JSON.stringify(req.body) } : {})
  });
  res.status(response.status).send(await response.text());
}
