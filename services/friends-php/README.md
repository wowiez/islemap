# Self-hosted IsleLiveMap friends

PHP 8.3 + Workerman, one worker with HTTP and WebSocket listeners in the same process. SQLite WAL stores accounts, device public keys, recovery hashes and friendships. Device private keys stay on the client. Latest positions live in RAM for 10 seconds, with no location history. Redis is optional when a future deployment needs multiple presence workers.

The default origin is `https://southtampanailsfl.com/api/islemap/`. HTTP routes are `account`, `friends`, `presence` (legacy clients) and `health`; the WebSocket route is `ws`. Nginx terminates TLS and proxies to loopback-only Docker ports. The rest of the existing PHP website is independent.

## Run

Build the image using `Dockerfile`; use `compose.yaml` with the existing persistent Docker data directory. Include `nginx-location.conf` inside the site's server block, validate configuration before reloading. Source deployed at `/opt/om/www/nails/api/islemap/`; SQLite at `/opt/om/data/islemap/islemap.sqlite` as viewed from the OM container, outside the web root. The companion PHP container sees that same directory as `/data`. Never serve database, backups, credential files or exports as static files.

The container restarts automatically and uses a read-only filesystem except `/data` and temporary files, with bounded memory and send buffers. One process intentionally owns HTTP and WebSocket state; do not raise worker count without adding shared state/coordination.

## Protocol and optimization

The first WebSocket text frame has `type: auth`, `payload` (exact JSON string), and lowercase `x-ilm-*` headers signed with the existing P-256 device key. The canonical route is `/api/islemap/ws`. No key or recovery secret is sent. Subsequent frames have `type: presence` and `data` matching the HTTP presence schema.

Context optionally includes `endpoint` (`udp:IPv4:port`) from the accepted player's Npcap position stream. When both peers provide endpoints, they must match, even if server names match. With no website player record, the client uses the fresh packet endpoint as its server identity. A stopped/stale capture provides no context. Legacy clients without endpoints retain conservative normalized-name matching.

The server pushes `{ type: snapshot, serverTime, friends }`, compatible with the existing presence response. It compares friend snapshots and batches at most once per second per connection. The client sends changed data at most once per second and heartbeats unchanged data every 5 seconds. No per-position database reads; account and friendship caches are invalidated immediately by HTTP mutations. Timestamps expire after 10 seconds, inactive sockets close after 30 seconds. Slow send buffers close the connection rather than grow indefinitely.

Only accepted friends on the same server/map receive a position when its owner enabled sharing. New connections cannot choose subscriptions or assert another user ID. Account recovery revokes old socket sessions. Signed HTTP/auth replay nonces survive restarts in SQLite for 120 seconds and are pruned every 30 seconds; position updates are not written to SQLite. Per-user request/message limits and expiring registration/invitation counters protect resource usage.

## Migration

1. Test PHP API and sockets against an isolated database.
2. Create `/data/maintenance` to block all account/friend/presence writes and socket upgrades; health remains available.
3. Set Vercel `ISLEMAP_ORIGIN` to this origin and deploy the tested bridge. Old clients forward unchanged signatures and payloads. Wait for in-flight source writes to finish and old signed requests to expire (at least 75 seconds).
4. Run the CLI-only `services/friends-api/export-mongo.js` with an ignored credential file and private export destination. Do not expose migration/export endpoints.
5. Pipe that private export to `php import.php` in the PHP container. Import refuses a nonempty target and commits atomically. Pipe the same export to `php verify-import.php`; every document must match and SQLite integrity/foreign keys must pass.
6. Remove the maintenance marker, test migrated identities through both origins, sharing/consent/expiry and real WebSockets. Keep MongoDB untouched as rollback backup. Restore the old Vercel deployment if origin verification fails.

Vercel can accept unchanged `/api/{route}` signatures only at the equivalent mapped PHP route. New clients sign the actual `/api/islemap/{route}` path. A bridge is not a redirect: legacy HTTP requests still count towards Vercel usage until users update.

## Verify

`php tests.php` checks consent, names, legacy signatures, expiry, sharing, recovery, replay across restart, migration and 500 friends. `node smoke.mjs HTTP_BASE WS_URL` runs against an isolated QA database and checks live pushed updates, permission changes and real expiry. Never point the fixture account creation script at an existing user database.

The .NET socket test verifies authentication, server push, advancing stale timestamps, sharing-off frames and reuse of the same connection without HTTP polling.
