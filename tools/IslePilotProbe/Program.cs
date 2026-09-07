using System.Diagnostics;
using TheIsleOverlay.IslePilot;

var credentialPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "IsleLiveMapData",
    "islepilot-overlay.credential");
var credentialStore = new IslePilotCredentialStore(credentialPath);
var accounts = await credentialStore.LoadAllAsync();
var credential = await credentialStore.LoadAsync();
if (credential is null)
{
    Console.WriteLine("credential=missing");
    return 2;
}

Console.WriteLine($"credential=valid steam=***{credential.SteamId[^4..]} persona={credential.PersonaName ?? "unknown"}");
Console.WriteLine($"credentialAccounts={accounts.Count}");
using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
var api = new IslePilotOverlayApiClient(
    httpClient,
    new IslePilotOverlayOptions { OverlayToken = credential.OverlayToken });

if (args.Contains("--session", StringComparer.OrdinalIgnoreCase))
{
    if (Process.GetProcessesByName("IsleLiveMap").Length > 0)
    {
        Console.WriteLine("session=skipped reason=IsleLiveMap-is-running");
        return 3;
    }
    var sockets = new List<IslePilotOverlayWebSocket>();
    var elapsed = Stopwatch.StartNew();
    await using var session = new IslePilotRealtimeSession(new MeasuredApi(api, elapsed),
        new IslePilotOverlayOptions { OverlayToken = credential.OverlayToken, PersonaName = credential.PersonaName },
        () => { var socket = new IslePilotOverlayWebSocket(); sockets.Add(socket); return socket; });
    using var duration = new CancellationTokenSource(TimeSpan.FromSeconds(180));
    TheIsleOverlay.Core.TelemetrySnapshot? previous = null;
    var positionChanges = 0;
    var statChanges = 0;
    var headingChanges = 0;
    double? lastPositionChange = null;
    var positionGaps = new List<double>();
    var nextReport = 0d;
    await foreach (var snapshot in session.WatchAsync(duration.Token))
    {
        if (previous?.Player?.Location is { } old && snapshot.Player?.Location is { } current && old != current)
        {
            positionChanges++;
            var at = elapsed.Elapsed.TotalSeconds;
            if (lastPositionChange is { } last) positionGaps.Add(at - last);
            Console.WriteLine($"movement t={at:F3}s gap={(lastPositionChange is { } prior ? (at - prior).ToString("F3") : "first")}s");
            lastPositionChange = at;
        }
        if (previous?.Player?.ExactMapHeadingDegrees is { } oldHeading &&
            snapshot.Player?.ExactMapHeadingDegrees is { } heading && oldHeading != heading)
            headingChanges++;
        if (previous?.Player?.ExactVitals is { } oldStats && snapshot.Player?.ExactVitals is { } currentStats && oldStats != currentStats)
            statChanges++;
        if (elapsed.Elapsed.TotalSeconds >= nextReport || snapshot.SessionState != previous?.SessionState)
        {
            Console.WriteLine($"session t={elapsed.Elapsed.TotalSeconds:F3}s state={snapshot.SessionState} online={snapshot.PlayerOnline} positionChanges={positionChanges} statChanges={statChanges}");
            nextReport = elapsed.Elapsed.TotalSeconds + 10;
        }
        previous = snapshot;
    }
    Console.WriteLine($"session=complete seconds={elapsed.Elapsed.TotalSeconds:F3} sockets={sockets.Count} messages={sockets.Sum(s => s.ReceivedMessages)} invalid={sockets.Sum(s => s.InvalidMessages)} live={sockets.Sum(s => s.LiveFrames)} positionChanges={positionChanges} statChanges={statChanges}");
    Console.WriteLine($"headingChanges={headingChanges}");
    if (positionGaps.Count > 0)
        Console.WriteLine($"observedPositionGap seconds min={positionGaps.Min():F3} avg={positionGaps.Average():F3} max={positionGaps.Max():F3}");
    return 0;
}

for (var sample = 1; sample <= 5; sample++)
{
    var meTask = MeasureAsync("me", api.GetMeAsync);
    var mapTask = MeasureAsync("map", api.GetMapAsync);
    var markersTask = MeasureAsync("markers", api.GetMarkersAsync);
    var results = await Task.WhenAll(meTask, mapTask, markersTask);
    Console.WriteLine($"sample={sample} {string.Join(' ', results)}");
    if (sample < 5)
    {
        await Task.Delay(500);
    }
}

var useUnusedAccount = args.Contains("--websocket-unused", StringComparer.OrdinalIgnoreCase);
if (!args.Contains("--websocket", StringComparer.OrdinalIgnoreCase) && !useUnusedAccount)
{
    return 0;
}

if (useUnusedAccount)
{
    credential = accounts.FirstOrDefault(account =>
        !string.Equals(account.SteamId, credential.SteamId, StringComparison.Ordinal));
    if (credential is null)
    {
        Console.WriteLine("websocket=skipped reason=no-unused-account");
        return 3;
    }

    Console.WriteLine($"websocketAccount=unused steam=***{credential.SteamId[^4..]}");
    var websocketAccountApi = new IslePilotOverlayApiClient(
        httpClient,
        new IslePilotOverlayOptions { OverlayToken = credential.OverlayToken });
    var websocketAccountMe = await websocketAccountApi.GetMeAsync();
    Console.WriteLine(
        $"websocketAccountOnline={websocketAccountMe.Online} hasData={websocketAccountMe.HasData}");
}
else if (Process.GetProcessesByName("IsleLiveMap").Length > 0)
{
    Console.WriteLine("websocket=skipped reason=IsleLiveMap-is-running");
    return 3;
}

await using var socket = new IslePilotOverlayWebSocket();
using var socketTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
var connectedAt = Stopwatch.StartNew();
await socket.ConnectAsync(credential.OverlayToken, socketTimeout.Token);
await socket.SendHelloAsync(credential.PersonaName, socketTimeout.Token);
Console.WriteLine($"websocket=connected elapsedMs={connectedAt.ElapsedMilliseconds}");
var frameCount = 0;
var lastFrameAt = Stopwatch.StartNew();
try
{
    await foreach (var frame in socket.ReadLiveAsync(socketTimeout.Token))
    {
        frameCount++;
        Console.WriteLine(
            $"websocket=live frame={frameCount} gapMs={lastFrameAt.ElapsedMilliseconds} " +
            $"hasDino={frame.HasDino} hasPosition={frame.Position is not null} hasVitals={frame.Health is not null || frame.Growth is not null}");
        lastFrameAt.Restart();
    }
}
catch (OperationCanceledException) when (socketTimeout.IsCancellationRequested)
{
}

Console.WriteLine($"websocket=complete frames={frameCount}");
return frameCount > 0 ? 0 : 4;

static async Task<string> MeasureAsync<T>(
    string name,
    Func<CancellationToken, Task<T>> request)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var stopwatch = Stopwatch.StartNew();
    try
    {
        var result = await request(timeout.Token);
        return $"{name}=ok:{stopwatch.ElapsedMilliseconds}ms:{Summary(result)}";
    }
    catch (Exception exception)
    {
        return $"{name}=error:{stopwatch.ElapsedMilliseconds}ms:{exception.GetType().Name}";
    }
}

static string Summary<T>(T result) => result switch
{
    IslePilotOverlayMeDto me =>
        $"online={me.Online},hasData={me.HasData},server={(string.IsNullOrWhiteSpace(me.Server) ? "none" : "yes")}",
    IslePilotOverlayMapDto map =>
        $"allowed={map.Allowed},pois={map.Pois.Count},markers={map.Markers.Count},calibration={map.Calibration is not null}",
    IslePilotOverlayMarkersDto markers =>
        $"ok={markers.Ok},markers={markers.Markers.Count}",
    _ => "decoded=true"
};
