// One expiring sorted set per active user replaces one Redis key per request.
// Keep legacy nonce checks so a pre-deploy request cannot be replayed after deploy.
export const AUTH_SCRIPT = `
local count=redis.call('INCR',KEYS[2])
if count==1 then redis.call('EXPIRE',KEYS[2],60) end
if count>180 then return -1 end
redis.call('ZREMRANGEBYSCORE',KEYS[1],'-inf',tonumber(ARGV[1])-120000)
if redis.call('EXISTS',KEYS[3])==1 or redis.call('ZSCORE',KEYS[1],ARGV[2]) then return 0 end
redis.call('ZADD',KEYS[1],ARGV[1],ARGV[2])
redis.call('EXPIRE',KEYS[1],120)
return 1`;
