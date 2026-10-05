<?php
declare(strict_types=1);
namespace IsleMap;

final class ApiError extends \RuntimeException { public function __construct(string $message, public int $status=400) { parent::__construct($message); } }
final class Service {
    public array $presence=[];
    private array $nonces=[];
    private array $limits=[];
    private array $users=[];
    private array $friends=[];
    private int $lastReplayPrune=0;
    public $onRevoke=null;
    public function __construct(public Store $store) {}
    public static function need(bool $ok,string $message,int $status=400): void { if (!$ok) throw new ApiError($message,$status); }
    public static function id(mixed $x): bool { return is_string($x) && preg_match('/^[a-f0-9]{32}$/D',$x)===1; }
    public static function name(mixed $x): string {
        self::need(is_string($x),'Tên không hợp lệ.');
        $x=\Normalizer::normalize(trim($x),\Normalizer::FORM_C);
        self::need(is_string($x) && mb_strlen($x)>=2 && mb_strlen($x)<=32 && !preg_match('/\p{C}/u',$x),'Tên cần 2–32 ký tự, không chứa ký tự điều khiển.'); return $x;
    }
    private static function key(mixed $pem): string {
        self::need(is_string($pem) && strlen($pem)<1024,'Khóa thiết bị không hợp lệ.');
        $key=@openssl_pkey_get_public($pem); $d=$key ? openssl_pkey_get_details($key) : false;
        self::need($d && $d['type']===OPENSSL_KEYTYPE_EC && ($d['ec']['curve_name']??'')==='prime256v1','Khóa thiết bị không hợp lệ.'); return $d['key'];
    }
    public function prune(int $now): void {
        if ($now-$this->lastReplayPrune>=30000) { $this->store->query('DELETE FROM replay WHERE at<=?',[$now-120000]); $this->lastReplayPrune=$now; }
        foreach ($this->presence as $id=>$p) if ($now-$p['at']>=10000) unset($this->presence[$id]);
        foreach ($this->nonces as $id=>$items) { foreach ($items as $n=>$t) if ($now-$t>=120000) unset($this->nonces[$id][$n]); if (!$this->nonces[$id]) unset($this->nonces[$id]); }
        foreach ($this->limits as $k=>$v) if ($now >= $v['until']) unset($this->limits[$k]);
        foreach ($this->users as $k=>$v) if ($now >= $v['until']) unset($this->users[$k]);
        foreach ($this->friends as $k=>$v) if ($now >= $v['until']) unset($this->friends[$k]);
    }
    public function limit(string $key,int $maximum,int $seconds,int $now): void {
        if (($this->limits[$key]['until']??0)<=$now) $this->limits[$key]=['n'=>0,'until'=>$now+$seconds*1000];
        self::need(++$this->limits[$key]['n']<=$maximum,'Quá nhiều yêu cầu, thử lại sau.',429);
    }
    public function user(string $id,int $now): ?array {
        if (($this->users[$id]['until']??0)<=$now) { $u=$this->store->user($id); if (!$u) return null; $this->users[$id]=['data'=>$u,'until'=>$now+300000]; }
        return $this->users[$id]['data'];
    }
    public function authenticate(array $h,string $raw,string $path,int $now): array {
        $id=$h['x-ilm-user']??null; $device=$h['x-ilm-device']??null; $nonce=$h['x-ilm-nonce']??null; $time=$h['x-ilm-time']??'';
        self::need(self::id($id)&&self::id($device)&&self::id($nonce)&&is_string($time)&&preg_match('/^\d{10,13}$/D',$time)&&abs($now-(int)$time)<60000,'Phiên xác thực không hợp lệ.',401);
        $u=$this->user($id,$now); $key=null; foreach ($u['devices']??[] as $d) if ($d['id']===$device) $key=$d['key'];
        $sig=base64_decode($h['x-ilm-signature']??'',true); $route=basename($path);
        // Both equivalent API paths are accepted for unchanged legacy clients behind Vercel.
        $valid=false; foreach (array_unique([$path,'/api/'.$route]) as $signedPath) {
            if ($key && $sig!==false && openssl_verify("POST\n$signedPath\n$time\n$nonce\n$raw",$sig,$key,OPENSSL_ALGO_SHA256)===1) { $valid=true; break; }
        }
        self::need($valid,'Thiết bị chưa được xác thực.',401);
        $this->limit('user:'.$id,180,60,$now);
        self::need(!isset($this->nonces[$id][$nonce]),'Yêu cầu đã được sử dụng.',401);
        try { $this->store->query('INSERT INTO replay(user_id,nonce,at) VALUES(?,?,?)',[$id,$nonce,$now]); }
        catch (\PDOException $e) { if (str_contains($e->getMessage(),'UNIQUE')) throw new ApiError('Yêu cầu đã được sử dụng.',401); throw $e; }
        $this->nonces[$id][$nonce]=$now; return $u;
    }
    private static function profile(array $u): array { return ['userId'=>$u['_id'],'name'=>$u['name'],'friendCode'=>$u['friendCode']]; }
    private function changed(string $id): void { unset($this->users[$id],$this->friends[$id]); }
    public function execute(string $route,array $h,string $raw,string $path,string $ip,int $now): array {
        self::need(strlen($raw)<=8192,'Payload không hợp lệ.');
        try { $b=json_decode($raw,true,32,JSON_THROW_ON_ERROR); } catch (\JsonException) { throw new ApiError('JSON không hợp lệ.'); }
        self::need(is_array($b)&&str_starts_with(ltrim($raw),'{'),'Dữ liệu không hợp lệ.');
        $action=$b['action']??'';
        if ($route==='account' && in_array($action,['create','recover'],true)) {
            $this->limit('registration:'.hash('sha256',$ip),12,3600,$now);
            self::need(self::id($b['deviceId']??null),'Thiết bị không hợp lệ.'); $key=self::key($b['publicKey']??null);
            $secret=rtrim(strtr(base64_encode(random_bytes(32)),'+/','-_'),'=');
            if ($action==='create') {
                $name=self::name($b['name']??null); $u=['_id'=>bin2hex(random_bytes(16)),'name'=>$name,'nameKey'=>mb_strtolower($name),'friendCode'=>strtoupper(bin2hex(random_bytes(6))),'recoveryHash'=>hash('sha256',$secret),'devices'=>[['id'=>$b['deviceId'],'key'=>$key]],'createdAt'=>gmdate('c')]; $this->store->saveUser($u,true);
            } else {
                $parts=explode('.',is_string($b['recoveryCode']??null)?$b['recoveryCode']:'');
                self::need(count($parts)===3&&$parts[0]==='ILM1'&&self::id($parts[1])&&preg_match('/^[A-Za-z0-9_-]{43}$/D',$parts[2]),'Mã khôi phục không hợp lệ.');
                $u=$this->store->user($parts[1]); self::need($u && hash_equals($u['recoveryHash'],hash('sha256',$parts[2])),'Mã khôi phục sai hoặc đã được dùng.',401);
                $u['recoveryHash']=hash('sha256',$secret); $u['devices']=[['id'=>$b['deviceId'],'key'=>$key]]; $this->store->saveUser($u);
                unset($this->presence[$u['_id']]); if ($this->onRevoke) ($this->onRevoke)($u['_id']);
            }
            $this->changed($u['_id']); return self::profile($u)+['recoveryCode'=>'ILM1.'.$u['_id'].'.'.$secret];
        }
        $u=$this->authenticate($h,$raw,$path,$now); $id=$u['_id'];
        if ($route==='account') {
            if ($action==='rename') { $u['name']=self::name($b['name']??null); $u['nameKey']=mb_strtolower($u['name']); $this->store->saveUser($u); $this->changed($id); if (isset($this->presence[$id])) $this->presence[$id]['name']=$u['name']; }
            elseif ($action==='recovery') { $secret=rtrim(strtr(base64_encode(random_bytes(32)),'+/','-_'),'='); $u['recoveryHash']=hash('sha256',$secret); $this->store->saveUser($u); $this->changed($id); return self::profile($u)+['recoveryCode'=>'ILM1.'.$id.'.'.$secret]; }
            else self::need($action==='me','Thao tác không hợp lệ.'); return self::profile($u);
        }
        if ($route==='friends') {
            if ($action==='request') {
                $this->limit('invitations:'.$id,20,3600,$now);
                $byName=is_string($b['name']??null); $code=is_string($b['code']??null)?strtoupper(trim($b['code'])):'';
                self::need($byName||preg_match('/^[A-F0-9]{12}$/D',$code),'Tên hoặc mã kết bạn không hợp lệ.');
                $other=$this->store->find($byName?'name_key':'friend_code',$byName?mb_strtolower(self::name($b['name'])):$code);
                self::need($other&&$other['_id']!==$id,'Không tìm thấy người dùng hoặc bạn đang nhập mã của mình.');
                foreach ([$id,$other['_id']] as $member) self::need(count($this->store->links($member))<500,'Danh sách bạn bè/lời mời đã đầy.',409);
                $ids=[$id,$other['_id']]; sort($ids,SORT_STRING); $l=['_id'=>implode(':',$ids),'members'=>[$id,$other['_id']],'from'=>$id,'to'=>$other['_id'],'state'=>'pending','at'=>gmdate('c')]; $this->store->saveLink($l,true);
            } elseif (in_array($action,['accept','reject','cancel','remove'],true)) {
                $other=$b['userId']??null; self::need(self::id($other)&&$other!==$id,'Người dùng không hợp lệ.'); $ids=[$id,$other]; sort($ids,SORT_STRING);
                $json=$this->store->query('SELECT document FROM links WHERE id=?',[implode(':',$ids)])->fetchColumn(); $l=$json===false?null:json_decode($json,true,512,JSON_THROW_ON_ERROR);
                self::need($l&&in_array($id,$l['members'],true)&&match($action){'accept','reject'=>$l['to']===$id&&$l['state']==='pending','cancel'=>$l['from']===$id&&$l['state']==='pending','remove'=>$l['state']==='accepted'},'Lời mời hoặc bạn bè không còn tồn tại.',404);
                if ($action==='accept') { $l['state']='accepted'; $l['acceptedAt']=gmdate('c'); $this->store->saveLink($l); } else $this->store->query('DELETE FROM links WHERE id=?',[$l['_id']]);
            } else self::need($action==='list','Thao tác không hợp lệ.');
            if (isset($l)) foreach ($l['members'] as $member) unset($this->friends[$member]);
            $result=[]; foreach ($this->store->links($id) as $l) { $other=$l['members'][0]===$id?$l['members'][1]:$l['members'][0]; $person=$this->user($other,$now); $result[]=['userId'=>$other,'name'=>$person['name']??'Người chơi','state'=>$l['state']==='accepted'?'accepted':($l['to']===$id?'incoming':'outgoing')]; }
            return self::profile($u)+['friends'=>$result];
        }
        if ($route==='presence') { $this->update($id,$u['name'],$b,$now); return $this->snapshot($id,$now); }
        throw new ApiError('Endpoint không tồn tại.',404);
    }
    public function update(string $id,string $name,array $b,int $now,?int $owner=null): void {
        $c=$b['context']??null; $p=$b['position']??null;
        $context=is_array($c)&&is_string($c['server']??null)&&trim($c['server'])!==''&&mb_strlen($c['server'])<=160&&($c['map']??null)==='gateway' ? ['server'=>mb_strtolower(\Normalizer::normalize(trim($c['server']))),'map'=>'gateway']:null;
        $position=is_array($p)&&is_numeric($p['x']??null)&&is_numeric($p['y']??null)&&is_finite((float)$p['x'])&&is_finite((float)$p['y'])&&$p['x']>=0&&$p['x']<=1&&$p['y']>=0&&$p['y']<=1 ? ['x'=>(float)$p['x'],'y'=>(float)$p['y'],'heading'=>isset($p['heading'])&&is_numeric($p['heading'])&&is_finite((float)$p['heading'])?(float)$p['heading']:null]:null;
        $sharing=($b['sharing']??false)===true&&$context!==null&&$position!==null;
        $this->presence[$id]=['at'=>$now,'sharing'=>$sharing,'server'=>$context['server']??null,'map'=>$context['map']??null,'position'=>$sharing?$position:null,'species'=>is_string($b['species']??null)?mb_substr($b['species'],0,64):'','name'=>$name,'owner'=>$owner];
    }
    public function snapshot(string $id,int $now): array {
        if (($this->friends[$id]['until']??0)<=$now) { $ids=[]; foreach ($this->store->links($id,true) as $l) $ids[]=$l['members'][0]===$id?$l['members'][1]:$l['members'][0]; $this->friends[$id]=['ids'=>$ids,'until'=>$now+300000]; }
        $own=$this->presence[$id]??null; $result=[];
        foreach ($this->friends[$id]['ids'] as $other) {
            $p=$this->presence[$other]??null; $online=$p && $now-$p['at']>=0 && $now-$p['at']<10000;
            $same=$online && $own && $own['server']!==null && $p['server']===$own['server'] && $p['map']===$own['map'];
            $result[]=['userId'=>$other,'online'=>(bool)$online,'name'=>$online?$p['name']:null,'species'=>$online?$p['species']:null,'position'=>$same&&$p['sharing']?$p['position']:null,'at'=>$online?$p['at']:null,'sameServer'=>(bool)$same];
        }
        return ['serverTime'=>$now,'friends'=>$result,'context'=>$own&&$own['server']!==null?['server'=>$own['server'],'map'=>$own['map']]:null];
    }
}
