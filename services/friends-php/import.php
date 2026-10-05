<?php
declare(strict_types=1);
require __DIR__.'/src/Store.php'; require __DIR__.'/src/Service.php';
use IsleMap\Store;
// Input is a private JSON export from MongoDB; never serve it from the web directory.
$source=json_decode(stream_get_contents(STDIN),true,512,JSON_THROW_ON_ERROR);
$store=new Store(getenv('ISLEMAP_DB')?:'/data/islemap.sqlite');
if ((int)$store->query('SELECT count(*) FROM users')->fetchColumn()!==0) throw new RuntimeException('Target database is not empty; refusing to replace data.');
$store->db->beginTransaction();
try {
    foreach ($source['users'] as $u) $store->saveUser($u,true);
    foreach ($source['links'] as $l) $store->saveLink($l,true);
    $users=(int)$store->query('SELECT count(*) FROM users')->fetchColumn(); $links=(int)$store->query('SELECT count(*) FROM links')->fetchColumn();
    if ($users!==count($source['users'])||$links!==count($source['links'])) throw new RuntimeException('Migration count mismatch');
    $store->db->commit(); echo json_encode(['users'=>$users,'links'=>$links,'integrity'=>$store->query('PRAGMA integrity_check')->fetchColumn()])."\n";
} catch (Throwable $e) { $store->db->rollBack(); throw $e; }
