<?php
declare(strict_types=1);
require __DIR__.'/src/Store.php'; require __DIR__.'/src/Service.php';
$source=json_decode(stream_get_contents(STDIN),true,512,JSON_THROW_ON_ERROR);
$store=new IsleMap\Store(getenv('ISLEMAP_DB')?:'/data/islemap.sqlite');
foreach (['users','links'] as $table) {
    $rows=$store->query('SELECT id,document FROM '.$table)->fetchAll();
    if (count($rows)!==count($source[$table])) throw new RuntimeException('Count mismatch');
    $expected=[]; foreach($source[$table] as $document) $expected[$document['_id']]=$document;
    foreach($rows as $row) if (json_decode($row['document'],true,512,JSON_THROW_ON_ERROR)!==$expected[$row['id']]) throw new RuntimeException('Document mismatch');
}
if ($store->query('PRAGMA foreign_key_check')->fetchAll() || $store->query('PRAGMA integrity_check')->fetchColumn()!=='ok') throw new RuntimeException('Integrity failure');
echo "Migration verified: every account field, device public key, recovery hash and friendship matches MongoDB export; SQLite integrity ok.\n";
