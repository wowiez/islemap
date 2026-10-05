# IsleLiveMap friends API

Vercel Node.js functions + MongoDB Atlas + Redis. No database credentials are shipped in the desktop client.

## Deploy

Production API: `https://wowie-theisle.vercel.app`. The initial deployment uses Vercel Drop with the files in this folder zipped at the archive root (omit `.env*` and `node_modules`), project Root Directory empty, framework Other, output `public`, build `npm test`, install `npm ci --ignore-scripts`, Node.js 22.x. Existing Vercel environment variables supply the database connections.

For future Git-based deployment:

Import `wowiez/islemap` into the Vercel project with domain `wowie-theisle.vercel.app`.
Set **Root Directory** to `services/friends-api`, framework **Other**, Node.js **22.x**.
Set Production environment variables `MONGODB_URI`, `REDIS_URL`; optional `MONGODB_DATABASE` defaults to `islelivemap`.
Use the Redis provider's TLS connection URL (`rediss://`) when TLS is enabled. Do not merely change the scheme if the endpoint does not support TLS.
Database access and Atlas network rules must allow the deployed API to connect.
Deploy and check `GET /api/health`: `{ "ok": true, "service": "IsleLiveMap Friends", "version": 1 }` confirms both databases are reachable.
Rotate any credentials previously pasted into chat; configure new values through the provider's integration or Vercel UI.

Run `npm ci --ignore-scripts`, `npm test` before deployment. Tests use in-memory stores and synthetic identities; never production credentials.

## Desktop flow

F8 → Bạn bè → enter a display name (2–32 characters) and create an account, or enter a recovery code.
The device's ECDSA P-256 private key and recovery code are DPAPI encrypted in `%LOCALAPPDATA%/IsleLiveMapData/friends-identity.dat`.
Public friend codes identify invitations, never authentication. Names are unique after trimming, NFC normalization and lowercasing; identity is a server-generated user ID. Friend requests can use an explicit name or public code, including when a name looks like a code. A partial unique nameKey index is created before legacy names are backfilled. For pre-existing duplicate names, the oldest account retains its name and later accounts receive an ID suffix; user IDs and friendships remain unchanged.
Export the recovery code with **LƯU MÃ KHÔI PHỤC** and keep that file private.
Recovering on a new computer preserves user ID and friendships, revokes old devices, rotates the one-use recovery secret and clears the previous live position; the recovered client uses the default sharing setting.
The current release supports recovery transfer, not simultaneous multi-device linking.

Share location is **ON on every app launch**. It can be turned off in F8; while off, the client does not upload coordinates. An account is required for friends, not to use the existing map.
Accepted friends are durable MongoDB documents; position/online presence lives in Redis for 10 seconds.
Sync runs at most once per second without overlapping requests; network failures back off, and stale markers disappear even during an outage.
Positions are only returned to accepted friends with an exact matching normalized game server name and map ID (`gateway`). Unknown or stale game context never shares positions.
Game positions are self-reported; this API does not certify which server a player is connected to.

## Protocol

Endpoints: `POST /api/account`, `/api/friends`, `/api/presence`; `GET /api/health`.
JSON envelope: `{ "payload": "<exact serialized inner JSON>" }`.
For authenticated requests: `X-ILM-User`, `X-ILM-Device`, `X-ILM-Time` (Unix milliseconds), `X-ILM-Nonce` (32 hex chars), `X-ILM-Signature` (base64 DER ECDSA SHA256).
Canonical signature: `METHOD\nPATH\nTIME\nNONCE\nPAYLOAD`. Maximum clock skew 60 seconds; nonce is single-use for 120 seconds.
Account actions: `create` (name, deviceId, publicKey PEM), `recover` (recoveryCode, deviceId, publicKey), `me`, `rename` (name), `recovery` (rotate secret).
Friends: `list`, `request` (code or name), `accept`, `reject`, `cancel`, `remove` (userId). Only the invitation recipient can accept/reject.
Presence: `sharing`, `context: { server, map }`, `position: { x, y, heading }` (normalized map coordinates), `species`.
API caches are disabled; invalid authentication, replay, unapproved friendships and invalid coordinates are denied. Registration and invitation rate limits are enforced.
No Steam, SBTC or IslePilot cookies/tokens are sent to this service.

## Limits

No cloud email login or password reset; without a saved recovery code or working original device, account recovery is unavailable.
Block lists, account deletion and device pairing are not part of this release.
At one sync/second, each active user produces about 3,600 API calls/hour plus database operations. Review Vercel/Redis/MongoDB quotas before wide rollout.


## Ephemeral storage and free-plan capacity

Positions are overwritten, never appended: one `ilm:presence:<user>` key, EX 10 seconds since last upload.
Anti-replay nonces use one sorted set per active user, prune entries older than 120 seconds and expire the key after 120 seconds without traffic.
The shared per-user rate counter expires after 60 seconds; registration/invitation counters expire after 3600 seconds. No application Redis key is permanent.
Friendships and identity stay in MongoDB. Accepted friend IDs are read using one MGET (up to 500), not one Redis request per friend. Name lookups for the list use a Map rather than a quadratic search.
This bounds memory over time but does not make 1-second synchronization unlimited. Redis Cloud's free 30 MB plan currently permits 30 connections, 100 ops/sec and 5 GB total monthly bandwidth; Vercel instance concurrency and active users also consume quota. See https://redis.io/docs/latest/operate/rc/subscriptions/view-essentials-subscription/essentials-plan-details/ .
The 500-friend test is a local correctness fixture, not a claim of 500 concurrent users supported on the free plan.
