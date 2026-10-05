<?php
declare(strict_types=1);
require __DIR__.'/vendor/autoload.php';
require __DIR__.'/src/Store.php';
require __DIR__.'/src/Service.php';
use IsleMap\{Store,Service,ApiError};
use Workerman\{Worker,Timer};
use Workerman\Protocols\Http\Response;

Worker::$logFile='/data/worker.log'; Worker::$pidFile='/data/worker.pid';
$http=new Worker('http://0.0.0.0:8080'); $http->count=1; $http->name='IsleMap';
$http->onWorkerStart=function($http) {
    $service=new Service(new Store(getenv('ISLEMAP_DB')?:'/data/islemap.sqlite'));
    $clients=[];
    $service->onRevoke=function($id) use (&$clients) { foreach ($clients as $c) if (($c->isleUser??null)===$id) $c->close(); };
    $http->onMessage=function($connection,$request) use ($service) {
        $headers=['Content-Type'=>'application/json; charset=utf-8','Cache-Control'=>'private, no-store'];
        try {
            clearstatcache(true,'/data/maintenance');
            $path=$request->path(); Service::need(preg_match('#^/api/islemap/(health|account|friends|presence)$#D',$path)===1,'Endpoint không tồn tại.',404); $route=basename($path);
            if ($route!=='health') Service::need(!is_file('/data/maintenance'),'Đang chuyển dữ liệu, vui lòng thử lại sau.',503);
            if ($route==='health') { $service->store->query('SELECT 1'); $result=['ok'=>true,'service'=>'IsleLiveMap Friends','version'=>2,'transport'=>'websocket','storage'=>'sqlite']; }
            else {
                Service::need($request->method()==='POST','Chỉ hỗ trợ POST.',405);
                Service::need(strlen($request->rawBody())<=12288,'Payload không hợp lệ.',413);
                $envelope=json_decode($request->rawBody(),true,32,JSON_THROW_ON_ERROR); Service::need(is_string($envelope['payload']??null),'Payload không hợp lệ.');
                // Origin sees only the gateway's verified address. Never trust client-supplied forwarding headers.
                $ip=$request->header('x-islemap-client-ip')?:$connection->getRemoteIp();
                $result=$service->execute($route,$request->header(),$envelope['payload'],$path,$ip,(int)floor(microtime(true)*1000));
            }
            $connection->send(new Response(200,$headers,json_encode($result,JSON_THROW_ON_ERROR|JSON_UNESCAPED_UNICODE)));
        } catch (\Throwable $e) {
            $status=$e instanceof ApiError?$e->status:($e instanceof \JsonException?400:503);
            $connection->send(new Response($status,$headers,json_encode(['error'=>$status===503?'API tạm thời không khả dụng.':($status===400&&$e instanceof \JsonException?'JSON không hợp lệ.':$e->getMessage())])));
        }
    };
    // A second listener in this same process shares RAM presence, replay guards and cache with HTTP.
    $ws=new Worker('websocket://0.0.0.0:8081'); $ws->name='IsleMap WebSocket';
    $ws->onConnect=function($c) use (&$clients) { $c->isleUser=null; $c->isleDevice=null; $c->isleOpened=time(); $c->isleSeen=time(); $c->isleLast=''; $c->maxSendBufferSize=262144; $clients[$c->id]=$c; };
    $ws->onWebSocketConnect=function($c,$request) { clearstatcache(true,'/data/maintenance'); if ($request->path()!=='/api/islemap/ws'||is_file('/data/maintenance')) $c->close(); };
    $ws->onBufferFull=function($c) { $c->close(); }; // Slow clients reconnect to a fresh snapshot; no unbounded queue.
    $ws->onMessage=function($c,$message) use ($service,&$clients) {
        try {
            Service::need(strlen($message)<=12288,'Payload không hợp lệ.',413);
            $m=json_decode($message,true,32,JSON_THROW_ON_ERROR); Service::need(is_array($m),'JSON không hợp lệ.'); $now=(int)floor(microtime(true)*1000);
            $c->isleSeen=time();
            if ($c->isleUser===null) {
                Service::need(($m['type']??null)==='auth'&&is_string($m['payload']??null)&&is_array($m['headers']??null),'Thiết bị chưa được xác thực.',401);
                $u=$service->authenticate(array_change_key_case($m['headers']),$m['payload'],'/api/islemap/ws',$now);
                $b=json_decode($m['payload'],true,32,JSON_THROW_ON_ERROR); Service::need(is_array($b),'Payload không hợp lệ.');
                $c->isleUser=$u['_id']; $c->isleDevice=$m['headers']['x-ilm-device'];
                foreach ($clients as $old) if ($old!==$c&&$old->isleUser===$c->isleUser&&$old->isleDevice===$c->isleDevice) $old->close();
                $service->update($u['_id'],$u['name'],$b,$now,$c->id);
                $c->send(json_encode(['type'=>'ready']));
            } else {
                Service::need(($m['type']??null)==='presence'&&is_array($m['data']??null),'Thao tác không hợp lệ.');
                $service->limit('ws:'.$c->isleUser,120,60,$now);
                $u=$service->user($c->isleUser,$now); Service::need($u!==null,'Thiết bị chưa được xác thực.',401);
                $service->update($c->isleUser,$u['name'],$m['data'],$now,$c->id);
            }
        } catch (\Throwable $e) { $c->send(json_encode(['type'=>'error','status'=>$e instanceof ApiError?$e->status:400,'error'=>$e instanceof ApiError?$e->getMessage():'Dữ liệu không hợp lệ.'])); $c->close(); }
    };
    $ws->onClose=function($c) use (&$clients,$service) { unset($clients[$c->id]); if ($c->isleUser && ($service->presence[$c->isleUser]['owner']??null)===$c->id) unset($service->presence[$c->isleUser]); };
    $ws->listen();
    Timer::add(1,function() use (&$clients,$service) {
        $now=(int)floor(microtime(true)*1000); $service->prune($now);
        foreach ($clients as $c) {
            if (time()-$c->isleSeen>=30) { $c->close(); continue; }
            if (!$c->isleUser) { if (time()-$c->isleOpened>=10) $c->close(); continue; }
            $snapshot=$service->snapshot($c->isleUser,$now); $value=json_encode([$snapshot['context'],$snapshot['friends']],JSON_THROW_ON_ERROR|JSON_UNESCAPED_UNICODE);
            if ($value!==$c->isleLast) { $c->isleLast=$value; $c->send(json_encode(['type'=>'snapshot']+$snapshot,JSON_THROW_ON_ERROR|JSON_UNESCAPED_UNICODE)); }
        }
    });
};
Worker::runAll();
