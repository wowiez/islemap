<?php
declare(strict_types=1);
require __DIR__.'/src/Store.php'; require __DIR__.'/src/Service.php';
use IsleMap\{Store,Service,ApiError};
function check(bool $ok,string $message): void { if (!$ok) throw new RuntimeException($message); }
$service=new Service(new Store(':memory:')); $now=1700000000000;
function actor(Service $s,string $name): array {
    global $now;
    $key=openssl_pkey_new(['private_key_type'=>OPENSSL_KEYTYPE_EC,'curve_name'=>'prime256v1']); $device=bin2hex(random_bytes(16));
    $result=$s->execute('account',[],json_encode(['action'=>'create','name'=>$name,'deviceId'=>$device,'publicKey'=>openssl_pkey_get_details($key)['key']]),'/api/islemap/account',$name,$now);
    return $result+['key'=>$key,'device'=>$device];
}
function call(Service $s,array $a,string $route,array $body,string $prefix='/api/islemap/'): array {
    global $now; $raw=json_encode($body,JSON_UNESCAPED_UNICODE|JSON_THROW_ON_ERROR); $nonce=bin2hex(random_bytes(16)); $path=$prefix.$route;
    openssl_sign("POST\n$path\n$now\n$nonce\n$raw",$sig,$a['key'],OPENSSL_ALGO_SHA256);
    return $s->execute($route,['x-ilm-user'=>$a['userId'],'x-ilm-device'=>$a['device'],'x-ilm-time'=>(string)$now,'x-ilm-nonce'=>$nonce,'x-ilm-signature'=>base64_encode($sig)],$raw,'/api/islemap/'.$route,'test',$now);
}
function denied(callable $f,int $status): void { try { $f(); } catch (ApiError $e) { check($e->status===$status,'Unexpected status'); return; } throw new RuntimeException('Expected rejection'); }
function position(string $server='sbtc',bool $sharing=true): array { return ['sharing'=>$sharing,'context'=>['server'=>$server,'map'=>'gateway'],'position'=>['x'=>0.4,'y'=>0.6,'heading'=>30],'species'=>'Ptera']; }
$a=actor($service,'Bạn A'); $b=actor($service,'Bạn B');
denied(fn()=>actor($service,'BẠN A'),409);
call($service,$a,'friends',['action'=>'request','name'=>'Bạn B']);
call($service,$b,'presence',position()); check(call($service,$a,'presence',position())['friends']===[],'Pending exposes presence');
denied(fn()=>call($service,$a,'friends',['action'=>'accept','userId'=>$b['userId']]),404);
call($service,$b,'friends',['action'=>'accept','userId'=>$a['userId']]);
check(call($service,$a,'presence',position(),'/api/')['friends'][0]['position']['x']===0.4,'Legacy signed path failed');
foreach (['SDVN #3','[SEA/VN]-SDVN-#3-X3','[SEA/VN]-SDVN-#3-X3 Grow','SDVN #3 X3 GROW','sdvn3123123123','3.sdvn.org'] as $alias) {
    call($service,$b,'presence',position($alias)); $snapshot=call($service,$a,'presence',position('sdvn3'));
    check($snapshot['friends'][0]['sameServer']&&$snapshot['friends'][0]['position']!==null,'SDVN #3 alias mismatch');
    check(call($service,$b,'presence',position($alias))['context']['server']===mb_strtolower($alias),'Legacy socket context changed');
}
foreach (['SDVN #1','SDVN #2','SDVN #30','SDVN','Other SDVN #3 server'] as $other) {
    call($service,$b,'presence',position($other)); check(call($service,$a,'presence',position('sdvn3'))['friends'][0]['position']===null,'Different SDVN server leaked');
}
foreach ([['SBTC Island','sbtc'],[' PANDORA ','pandora'],['DinoVietNam','dinovietnam'],['Some  Other Server','some other server']] as [$left,$right]) {
    call($service,$b,'presence',position($left)); check(call($service,$a,'presence',position($right))['friends'][0]['sameServer'],'Other same-server mismatch');
}
call($service,$b,'presence',position('DinoVietNam Premium')); check(!call($service,$a,'presence',position('DinoVietNam'))['friends'][0]['sameServer'],'Premium merged with regular server');
$packet=position('udp:192.0.2.10:7777'); $packet['context']['endpoint']='udp:192.0.2.10:7777';
$webAndPacket=position('SDVN #3'); $webAndPacket['context']['endpoint']='udp:192.0.2.10:7777';
call($service,$b,'presence',$packet); $snap=call($service,$a,'presence',$webAndPacket);
check($snap['friends'][0]['sameServer']&&$snap['friends'][0]['position']!==null,'Packet-only friend requires web');
check($snap['context']===['server'=>'sdvn #3','map'=>'gateway','endpoint'=>'udp:192.0.2.10:7777'],'Socket endpoint context was lost');
$different=$webAndPacket; $different['context']['endpoint']='udp:192.0.2.10:7778';
call($service,$b,'presence',$different); check(!call($service,$a,'presence',$webAndPacket)['friends'][0]['sameServer'],'Same IP different game port leaked');
$different['context']['endpoint']='udp:192.0.2.11:7777';
call($service,$b,'presence',$different); check(!call($service,$a,'presence',$webAndPacket)['friends'][0]['sameServer'],'Same name different endpoint leaked');
// Renaming keeps the account, recovery key, device and friendships, including Unicode NFC uniqueness.
$before=$service->store->user($b['userId']);
$renamed=call($service,$b,'account',['action'=>'rename','name'=>"Kha\u{0301}ng"]);
check($renamed['name']==='Kháng'&&$renamed['userId']===$b['userId']&&$renamed['friendCode']===$b['friendCode'],'Unicode rename changed identity');
$after=$service->store->user($b['userId']); check($before['devices']===$after['devices']&&$before['recoveryHash']===$after['recoveryHash'],'Rename changed keys');
check($service->presence[$b['userId']]['name']==='Kháng','Live map name stale after rename');
check(call($service,$a,'friends',['action'=>'list'])['friends'][0]['name']==='Kháng','Friend list name stale');
denied(fn()=>call($service,$a,'account',['action'=>'rename','name'=>"KHA\u{0301}NG"]),409);
check($service->store->user($a['userId'])['name']==='Bạn A','Rejected rename changed account');
call($service,$b,'presence',position('other')); check(call($service,$a,'presence',position())['friends'][0]['position']===null,'Cross server leak');
call($service,$b,'presence',position('sbtc',false)); check(call($service,$a,'presence',position())['friends'][0]['position']===null,'Sharing off leak');
call($service,$b,'presence',position()); $now+=10000; $service->prune($now); check(call($service,$a,'presence',position())['friends'][0]['online']===false,'TTL failed');
call($service,$b,'presence',position()); call($service,$a,'friends',['action'=>'remove','userId'=>$b['userId']]); check(call($service,$a,'presence',position())['friends']===[],'Unfriend cache stale');
// Recovery is one-use and revokes the old signing device.
$recovered=$service->execute('account',[],json_encode(['action'=>'recover','recoveryCode'=>$a['recoveryCode'],'deviceId'=>bin2hex(random_bytes(16)),'publicKey'=>openssl_pkey_get_details($b['key'])['key']]),'/api/islemap/account','recovery',$now);
check($recovered['userId']===$a['userId'],'Recovery changed ID'); denied(fn()=>call($service,$a,'friends',['action'=>'list']),401);
// A migrated account preserves the exact device key, recovery hash and friendships.
$copy=new Service(new Store(':memory:'));
foreach ($service->store->query('SELECT document FROM users')->fetchAll() as $r) $copy->store->saveUser(json_decode($r['document'],true),true);
check(call($copy,$b,'friends',['action'=>'list'])['userId']===$b['userId'],'Migration signature failed');
// A captured signed request cannot be replayed or changed to a different payload.
$raw=json_encode(['action'=>'list']); $nonce=bin2hex(random_bytes(16));
openssl_sign("POST\n/api/friends\n$now\n$nonce\n$raw",$signature,$b['key'],OPENSSL_ALGO_SHA256);
$headers=['x-ilm-user'=>$b['userId'],'x-ilm-device'=>$b['device'],'x-ilm-time'=>(string)$now,'x-ilm-nonce'=>$nonce,'x-ilm-signature'=>base64_encode($signature)];
$copy->execute('friends',$headers,$raw,'/api/islemap/friends','test',$now);
denied(fn()=>$copy->execute('friends',$headers,$raw,'/api/islemap/friends','test',$now),401);
$restarted=new Service($copy->store);
denied(fn()=>$restarted->execute('friends',$headers,$raw,'/api/islemap/friends','test',$now),401);
denied(fn()=>$copy->execute('friends',$headers,'{"action":"remove"}','/api/islemap/friends','test',$now),401);
// 500 accepted friends are cached; unchanged snapshots never query SQLite per friend.
$many=actor($copy,'Many Friends');
for ($i=0;$i<500;$i++) {
    $other=bin2hex(random_bytes(16)); $name='Fixture '.$i;
    $copy->store->saveUser(['_id'=>$other,'name'=>$name,'nameKey'=>strtolower($name),'friendCode'=>strtoupper(bin2hex(random_bytes(6))),'devices'=>[],'recoveryHash'=>'fixture'],true);
    $ids=[$many['userId'],$other]; sort($ids);
    $copy->store->saveLink(['_id'=>implode(':',$ids),'members'=>$ids,'from'=>$many['userId'],'to'=>$other,'state'=>'accepted'],true);
    $copy->update($other,$name,position($i%2?'other':'sbtc'),$now);
}
$copy->update($many['userId'],$many['name'],position(),$now);
$start=hrtime(true); $snapshot=$copy->snapshot($many['userId'],$now);
check(count($snapshot['friends'])===500,'500-friend snapshot failed');
check(count(array_filter($snapshot['friends'],fn($p)=>$p['position']!==null))===250,'500-friend server filtering failed');
for($i=0;$i<100;$i++) $copy->snapshot($many['userId'],$now);
echo '500-friend first snapshot + 100 cached snapshots: '.round((hrtime(true)-$start)/1e6,1)." ms (local fixture, not capacity guarantee).\n";
echo "PHP API checks passed: unique name, legacy signature, consent, cross-server, sharing, expiry, unfriend cache, recovery, migration.\n";
