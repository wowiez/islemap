import fs from 'node:fs';
import { MongoClient } from 'mongodb';
// CLI only; never expose this through a public API. No credentials or documents go to stdout.
const [credentialPath, destination] = process.argv.slice(2);
if (!credentialPath || !destination) throw new Error('Expected credential file and private export destination');
const text = fs.readFileSync(credentialPath,'utf8');
const uri = text.match(/mongodb(?:\+srv)?:\/\/[^\s"'`]+/)?.[0];
if (!uri) throw new Error('MongoDB configuration not found');
const client = new MongoClient(uri,{serverSelectionTimeoutMS:10000});
try {
  await client.connect();
  const db = client.db(process.env.MONGODB_DATABASE || 'islelivemap');
  const users = await db.collection('overlay_users').find({}).toArray();
  const links = await db.collection('overlay_friendships').find({}).toArray();
  fs.writeFileSync(destination,JSON.stringify({users,links}),{mode:0o600,flag:'wx'});
  console.log(JSON.stringify({users:users.length,links:links.length,exported:true}));
} catch { process.exitCode=1; console.error('MongoDB export failed; credentials and database contents withheld.'); }
finally { await client.close(); }
