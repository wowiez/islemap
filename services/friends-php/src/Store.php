<?php
declare(strict_types=1);
namespace IsleMap;

final class Store {
    public \PDO $db;
    public function __construct(string $path) {
        $this->db = new \PDO('sqlite:' . $path, null, null, [\PDO::ATTR_ERRMODE => \PDO::ERRMODE_EXCEPTION, \PDO::ATTR_DEFAULT_FETCH_MODE => \PDO::FETCH_ASSOC]);
        $this->db->exec('PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;');
        $this->db->exec('CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY, name TEXT NOT NULL, name_key TEXT NOT NULL UNIQUE, friend_code TEXT NOT NULL UNIQUE, document TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS links(id TEXT PRIMARY KEY, a TEXT NOT NULL REFERENCES users(id), b TEXT NOT NULL REFERENCES users(id), state TEXT NOT NULL, document TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS links_a ON links(a,state); CREATE INDEX IF NOT EXISTS links_b ON links(b,state);
            CREATE TABLE IF NOT EXISTS replay(user_id TEXT NOT NULL, nonce TEXT NOT NULL, at INTEGER NOT NULL, PRIMARY KEY(user_id,nonce)); CREATE INDEX IF NOT EXISTS replay_at ON replay(at);');
    }
    public function query(string $sql, array $args = []): \PDOStatement { $q=$this->db->prepare($sql); $q->execute($args); return $q; }
    public function user(string $id): ?array { $r=$this->query('SELECT document FROM users WHERE id=?',[$id])->fetchColumn(); return $r === false ? null : json_decode($r,true,512,JSON_THROW_ON_ERROR); }
    public function find(string $column, string $value): ?array {
        if (!in_array($column,['name_key','friend_code'],true)) throw new \LogicException('Invalid lookup');
        $r=$this->query("SELECT document FROM users WHERE $column=?",[$value])->fetchColumn(); return $r===false ? null : json_decode($r,true,512,JSON_THROW_ON_ERROR);
    }
    public function saveUser(array $u, bool $create=false): void {
        $args=[$u['name'],$u['nameKey'],$u['friendCode'],json_encode($u,JSON_THROW_ON_ERROR),$u['_id']];
        try { $this->query($create ? 'INSERT INTO users(name,name_key,friend_code,document,id) VALUES(?,?,?,?,?)' : 'UPDATE users SET name=?,name_key=?,friend_code=?,document=? WHERE id=?',$args); }
        catch (\PDOException $e) { if (str_contains($e->getMessage(),'UNIQUE')) throw new ApiError('Tên đã được sử dụng. Hãy chọn tên khác.',409); throw $e; }
    }
    public function links(string $id, bool $accepted=false): array {
        $q=$this->query('SELECT document FROM links WHERE (a=? OR b=?)'.($accepted ? " AND state='accepted'" : '').' LIMIT 501',[$id,$id]);
        return array_map(fn($r)=>json_decode($r['document'],true,512,JSON_THROW_ON_ERROR),$q->fetchAll());
    }
    public function saveLink(array $l, bool $create=false): void {
        try { $this->query($create ? 'INSERT INTO links(a,b,state,document,id) VALUES(?,?,?,?,?)' : 'UPDATE links SET a=?,b=?,state=?,document=? WHERE id=?',[$l['members'][0],$l['members'][1],$l['state'],json_encode($l,JSON_THROW_ON_ERROR),$l['_id']]); }
        catch (\PDOException $e) { if (str_contains($e->getMessage(),'UNIQUE')) throw new ApiError('Đã có lời mời hoặc đã là bạn bè.',409); throw $e; }
    }
}
