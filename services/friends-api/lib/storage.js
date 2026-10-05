import { MongoClient } from 'mongodb';
import { createClient } from 'redis';
import { ensureUniqueNames } from './names.js';

let connection;
export async function storage() {
  if (!connection) connection = connect().catch(error => { connection = null; throw error; });
  const store = await connection;
  if (!store.redis.isReady) {
    connection = null;
    await store.close();
    throw new Error('Redis disconnected');
  }
  return store;
}
async function connect() {
  if (!process.env.MONGODB_URI || !process.env.REDIS_URL) throw new Error('Missing configuration');
  const mongo = new MongoClient(process.env.MONGODB_URI, { maxPoolSize: 5, serverSelectionTimeoutMS: 5000 });
  const redis = createClient({ url: process.env.REDIS_URL, socket: { connectTimeout: 5000, reconnectStrategy: false }, disableOfflineQueue: true });
  redis.on('error', () => {});
  try {
    await Promise.all([mongo.connect(), redis.connect()]);
    const db = mongo.db(process.env.MONGODB_DATABASE || 'islelivemap');
    const users = db.collection('overlay_users');
    const links = db.collection('overlay_friendships');
    await Promise.all([
      users.createIndex({ friendCode: 1 }, { unique: true }),
      ensureUniqueNames(users),
      links.createIndex({ members: 1, state: 1 })
    ]);
    return { users, links, redis, close: async () => { if (redis.isOpen) redis.destroy(); await mongo.close().catch(() => {}); } };
  } catch (error) {
    await mongo.close().catch(() => {});
    if (redis.isOpen) redis.destroy();
    throw error;
  }
}
