import { nameKey } from './rules.js';

export async function ensureUniqueNames(users) {
  // The partial index protects new writes while legacy accounts are backfilled.
  await users.createIndex({ nameKey: 1 }, { unique: true, partialFilterExpression: { nameKey: { $type: 'string' } } });
  const legacy = await users.find({ nameKey: { $exists: false } }).sort({ createdAt: 1, _id: 1 }).toArray();
  for (const user of legacy) {
    let displayName = user.name;
    for (let attempt = 0; attempt < 5; attempt++) {
      try {
        await users.updateOne({ _id: user._id, nameKey: { $exists: false } }, { $set: { name: displayName, nameKey: nameKey(displayName) } });
        break;
      } catch (error) {
        if (error.code !== 11000 || attempt === 4) throw error;
        const suffix = user._id.slice(0, 8) + (attempt ? String(attempt) : '');
        displayName = user.name.slice(0, 32 - suffix.length - 1).trimEnd() + '-' + suffix;
      }
    }
  }
}
