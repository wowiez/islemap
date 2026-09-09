using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.IslePilot;

public sealed class IslePilotRealtimeSession : ITelemetrySession
{
    private readonly IIslePilotOverlayApiClient _apiClient;
    private readonly IslePilotOverlayOptions _options;
    private readonly Func<IIslePilotOverlayWebSocket> _socketFactory;
    private readonly IslePilotReconnectBackoff _backoff;
    private readonly Func<TimeSpan, CancellationToken, Task> _reconnectDelay;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly IDisposable? _ownedResource;
    private readonly IslePilotOverlayStateReducer _reducer;
    private readonly Channel<TelemetrySnapshot> _snapshots;
    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly object _stateGate = new();
    private readonly Dictionary<string, string> _activeRequests = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _meRequestGate = new(1, 1);
    private readonly SemaphoreSlim _mapRequestGate = new(1, 1);
    private readonly SemaphoreSlim _markersRequestGate = new(1, 1);
    private readonly Channel<bool> _mapRefreshSignals = CreateRefreshSignalChannel();
    private readonly Channel<bool> _markersRefreshSignals = CreateRefreshSignalChannel();

    private Task? _runTask;
    private int _watchStarted;
    private int _disposed;

    public IslePilotRealtimeSession(
        IIslePilotOverlayApiClient apiClient,
        IslePilotOverlayOptions options,
        Func<IIslePilotOverlayWebSocket>? socketFactory = null)
        : this(
            apiClient,
            options,
            socketFactory ?? (() => new IslePilotOverlayWebSocket()),
            new IslePilotReconnectBackoff(),
            static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
            static () => DateTimeOffset.UtcNow)
    {
    }

    public static IslePilotRealtimeSession Create(IslePilotOverlayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        try
        {
            return new IslePilotRealtimeSession(
                new IslePilotOverlayApiClient(httpClient, options),
                options,
                static () => new IslePilotOverlayWebSocket(),
                new IslePilotReconnectBackoff(),
                static (delay, cancellationToken) => Task.Delay(delay, cancellationToken),
                static () => DateTimeOffset.UtcNow,
                httpClient);
        }
        catch
        {
            httpClient.Dispose();
            throw;
        }
    }

    internal IslePilotRealtimeSession(
        IIslePilotOverlayApiClient apiClient,
        IslePilotOverlayOptions options,
        Func<IIslePilotOverlayWebSocket> socketFactory,
        IslePilotReconnectBackoff backoff,
        Func<TimeSpan, CancellationToken, Task> reconnectDelay,
        Func<DateTimeOffset> utcNow,
        IDisposable? ownedResource = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _socketFactory = socketFactory ?? throw new ArgumentNullException(nameof(socketFactory));
        _backoff = backoff ?? throw new ArgumentNullException(nameof(backoff));
        _reconnectDelay = reconnectDelay ?? throw new ArgumentNullException(nameof(reconnectDelay));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _ownedResource = ownedResource;

        ValidateOptions(options);
        _reducer = new IslePilotOverlayStateReducer(
            options.LiveDataLifetime,
            options.PositionFallbackAfter,
            options.MeRefreshInterval + options.RestRequestTimeout * (options.RestTimeoutRetryCount + 1) +
            options.RestTimeoutRetryDelay * options.RestTimeoutRetryCount +
            options.AuthenticationRetryDelay * options.AuthenticationRetryCount);
        _snapshots = Channel.CreateBounded<TelemetrySnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public async IAsyncEnumerable<TelemetrySnapshot> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (Interlocked.Exchange(ref _watchStarted, 1) != 0)
        {
            throw new InvalidOperationException("An IslePilot telemetry session can only be watched once.");
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _disposeCancellation.Token);
        _runTask = RunAsync(linkedCancellation.Token);

        try
        {
            while (true)
            {
                bool canRead;
                try
                {
                    canRead = await _snapshots.Reader.WaitToReadAsync(linkedCancellation.Token);
                }
                catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
                {
                    break;
                }

                if (!canRead)
                {
                    break;
                }

                while (_snapshots.Reader.TryRead(out var snapshot))
                {
                    yield return snapshot;
                }
            }
        }
        finally
        {
            linkedCancellation.Cancel();
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _disposeCancellation.Cancel();
            var runTask = Volatile.Read(ref _runTask);
            if (runTask is not null)
            {
                try
                {
                    await runTask;
                }
                catch (OperationCanceledException) when (_disposeCancellation.IsCancellationRequested)
                {
                }
            }
        }
        finally
        {
            _ownedResource?.Dispose();
            _meRequestGate.Dispose();
            _mapRequestGate.Dispose();
            _markersRequestGate.Dispose();
            _disposeCancellation.Dispose();
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        Exception? completionError = null;
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            UpdateState(reducer => reducer.SetSessionState(TelemetrySessionState.Connecting));
            // /me and /map are independent request types. Start them in parallel;
            // each endpoint still has its own strict single-flight queue.
            var initialMeTask = BootstrapMeAsync(runCancellation.Token);

            var tasks = new[]
            {
                RunGuardedAsync(
                    async cancellation =>
                    {
                        await initialMeTask;
                        await PollMeAsync(cancellation);
                    },
                    runCancellation),
                RunGuardedAsync(PollMapAsync, runCancellation),
                RunGuardedAsync(
                    async cancellation =>
                    {
                        await initialMeTask;
                        await PollMarkersAsync(cancellation);
                    },
                    runCancellation),
                RunGuardedAsync(RunWebSocketAsync, runCancellation),
                RunGuardedAsync(MonitorStaleDataAsync, runCancellation)
            };

            await Task.WhenAll(tasks);
        }
        catch (TelemetryAuthenticationException)
        {
            runCancellation.Cancel();
            UpdateState(reducer => reducer.SetSessionState(TelemetrySessionState.AuthenticationRequired));
        }
        catch (OperationCanceledException) when (runCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            completionError = exception;
        }
        finally
        {
            runCancellation.Cancel();
            _snapshots.Writer.TryComplete(completionError);
        }
    }

    private async Task BootstrapMeAsync(CancellationToken cancellationToken)
    {
        var requestStartedAt = _utcNow();
        try
        {
            var me = await ExecuteRestRequestAsync(
                _apiClient.GetMeAsync,
                _meRequestGate,
                "/ME",
                cancellationToken,
                confirmAuthenticationFailure: true);
            UpdateState(reducer => reducer.ApplyMe(me, _utcNow(), requestStartedAt));
        }
        catch (TelemetryAuthenticationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (IsRetryableRestFailure(exception, cancellationToken))
        {
        }

        // WebSocket startup is independent and uses the cached persona name when
        // /me has not completed yet. A slow identity response cannot delay live data.
    }

    private async Task PollMeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            await Task.Delay(_options.MeRefreshInterval, cancellationToken);
            var requestStartedAt = _utcNow();
            try
            {
                var me = await ExecuteRestRequestAsync(
                    _apiClient.GetMeAsync,
                    _meRequestGate,
                    "/ME",
                    cancellationToken,
                    confirmAuthenticationFailure: true);
                UpdateState(reducer => reducer.ApplyMe(me, _utcNow(), requestStartedAt));
            }
            catch (TelemetryAuthenticationException)
            {
                throw;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRetryableRestFailure(exception, cancellationToken))
            {
            }
        }
    }

    private async Task PollMapAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pollStartedAt = _utcNow();
            try
            {
                var map = await ExecuteRestRequestAsync(
                    _apiClient.GetMapAsync,
                    _mapRequestGate,
                    "/MAP",
                    cancellationToken);
                UpdateState(reducer => reducer.ApplyMap(map, _utcNow(), pollStartedAt));
            }
            catch (TelemetryAuthenticationException)
            {
                // A map endpoint can reject one feature while the overlay token
                // remains valid. Only /me may invalidate the active session.
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRetryableRestFailure(exception, cancellationToken))
            {
            }

            await WaitForNextPollAsync(
                _options.MapRefreshInterval,
                pollStartedAt,
                _mapRefreshSignals.Reader,
                cancellationToken);
        }
    }

    public Task<IslePilotOverlayGarageDto> GetGarageAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _apiClient.GetGarageAsync(cancellationToken);
    }

    public Task<IslePilotOverlayGarageCommandDto> ParkGarageDinoAsync(
        string step,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _apiClient.ParkGarageDinoAsync(step, cancellationToken);
    }

    public Task<IslePilotOverlayGarageCommandDto> RestoreGarageDinoAsync(
        string dinoId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _apiClient.RestoreGarageDinoAsync(dinoId, cancellationToken);
    }

    public Task<IslePilotOverlayGarageCommandStatusDto> GetGarageCommandStatusAsync(
        string commandId,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return _apiClient.GetGarageCommandStatusAsync(commandId, cancellationToken);
    }

    private async Task PollMarkersAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var pollStartedAt = _utcNow();
            if (ReadUsesDedicatedMarkers())
            {
                try
                {
                    var markers = await ExecuteRestRequestAsync(
                        _apiClient.GetMarkersAsync,
                        _markersRequestGate,
                        "/MARKERS",
                        cancellationToken);
                    if (markers.Ok)
                    {
                        UpdateState(reducer => reducer.ApplyMarkers(markers, _utcNow(), pollStartedAt));
                    }
                }
                catch (TelemetryAuthenticationException)
                {
                    // Dedicated markers can be forbidden independently of /me.
                    // Keep the account and the remaining telemetry streams alive.
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception) when (IsRetryableRestFailure(exception, cancellationToken))
                {
                }
            }

            await WaitForNextPollAsync(
                _options.MarkersRefreshInterval,
                pollStartedAt,
                _markersRefreshSignals.Reader,
                cancellationToken);
        }
    }

    private async Task<T> ExecuteRestRequestAsync<T>(
        Func<CancellationToken, Task<T>> request,
        SemaphoreSlim requestGate,
        string endpoint,
        CancellationToken cancellationToken,
        bool confirmAuthenticationFailure = false)
    {
        // Each endpoint owns one gate. Repeated requests of the same type can
        // never overlap or accumulate, while one /me and one /map may run in
        // parallel because they are independent data streams.
        await requestGate.WaitAsync(cancellationToken);
        try
        {
            var attempt = 0;
            var timeoutRetries = 0;
            var authenticationRetries = 0;
            while (true)
            {
                SetRequestActivity(endpoint, attempt == 0 ? "REQUESTING" : "RETRYING");
                using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                requestCancellation.CancelAfter(_options.RestRequestTimeout);
                try
                {
                    var result = await request(requestCancellation.Token);
                    requestCancellation.Token.ThrowIfCancellationRequested();
                    return result;
                }
                catch (TelemetryAuthenticationException) when (
                    confirmAuthenticationFailure &&
                    authenticationRetries < _options.AuthenticationRetryCount)
                {
                    authenticationRetries++;
                    SetRequestActivity(endpoint, "VERIFYING SESSION");
                    await Task.Delay(_options.AuthenticationRetryDelay, cancellationToken);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested &&
                    timeoutRetries < _options.RestTimeoutRetryCount)
                {
                    timeoutRetries++;
                    SetRequestActivity(endpoint, "RETRYING");
                    await Task.Delay(_options.RestTimeoutRetryDelay, cancellationToken);
                }
                finally
                {
                    SetRequestActivity(endpoint, null);
                }

                attempt++;
            }
        }
        finally
        {
            requestGate.Release();
        }
    }

    private async Task RunWebSocketAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var socket = _socketFactory();
                using var connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                connectCancellation.CancelAfter(_options.WebSocketConnectTimeout);
                try
                {
                    await socket.ConnectAsync(_options.OverlayToken, connectCancellation.Token);
                    await socket.SendHelloAsync(ReadPersonaName(), connectCancellation.Token);
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested &&
                    connectCancellation.IsCancellationRequested)
                {
                    throw new WebSocketException("The IslePilot WebSocket handshake timed out.");
                }

                // A successful handshake is no longer "reconnecting". /ows is
                // event-driven and can stay silent for longer than its former
                // inactivity watchdog, so REST remains the visible fallback
                // until a fresh live frame arrives.
                _backoff.Reset();
                UpdateState(reducer => reducer.SetSessionState(TelemetrySessionState.Stale));
                await foreach (var live in socket.ReadLiveAsync(cancellationToken))
                {
                    UpdateState(reducer => reducer.ApplyLive(live, _utcNow()));
                }

                throw new WebSocketException("The IslePilot WebSocket closed.");
            }
            catch (TelemetryAuthenticationException)
            {
                // WebSocket authorization can fail during a server deploy while
                // the REST token is still valid. Let /me be the authority and
                // keep reconnecting with the normal bounded backoff.
                UpdateState(reducer => reducer.SetSessionState(TelemetrySessionState.Reconnecting));
                RequestFallbackPositionRefresh();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (IsRecoverableSocketFailure(exception))
            {
                UpdateState(reducer => reducer.SetSessionState(TelemetrySessionState.Reconnecting));
                RequestFallbackPositionRefresh();
            }

            await _reconnectDelay(_backoff.NextDelay(), cancellationToken);
        }
    }

    private async Task MonitorStaleDataAsync(CancellationToken cancellationToken)
    {
        var intervalMilliseconds = Math.Clamp(
            _options.LiveDataLifetime.TotalMilliseconds / 4d,
            100d,
            1000d);
        var interval = TimeSpan.FromMilliseconds(intervalMilliseconds);

        while (true)
        {
            await Task.Delay(interval, cancellationToken);
            PublishSnapshot();
        }
    }

    private async Task RunGuardedAsync(
        Func<CancellationToken, Task> action,
        CancellationTokenSource runCancellation)
    {
        try
        {
            await action(runCancellation.Token);
        }
        catch (Exception) when (runCancellation.IsCancellationRequested)
        {
            // Some async transports surface disposal-specific exceptions when
            // their pending read is torn down. Session cancellation is still a
            // normal shutdown and must not fault the whole telemetry pipeline.
        }
        catch
        {
            runCancellation.Cancel();
            throw;
        }
    }

    private void UpdateState(Action<IslePilotOverlayStateReducer> update)
    {
        lock (_stateGate)
        {
            update(_reducer);
            _snapshots.Writer.TryWrite(BuildSnapshot());
        }
    }

    private void PublishSnapshot()
    {
        lock (_stateGate)
        {
            _snapshots.Writer.TryWrite(BuildSnapshot());
        }
    }

    private void RequestFallbackPositionRefresh()
    {
        _mapRefreshSignals.Writer.TryWrite(true);
        _markersRefreshSignals.Writer.TryWrite(true);
    }

    private async Task WaitForNextPollAsync(
        TimeSpan interval,
        DateTimeOffset pollStartedAt,
        ChannelReader<bool> refreshSignals,
        CancellationToken cancellationToken)
    {
        var elapsed = _utcNow() - pollStartedAt;
        var remainingDelay = interval - elapsed;
        if (remainingDelay < TimeSpan.FromMilliseconds(250))
        {
            remainingDelay = TimeSpan.FromMilliseconds(250);
        }

        using var waitCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delayTask = Task.Delay(remainingDelay, waitCancellation.Token);
        var refreshTask = refreshSignals.ReadAsync(waitCancellation.Token).AsTask();
        var completed = await Task.WhenAny(delayTask, refreshTask);
        try
        {
            await completed;
        }
        finally
        {
            waitCancellation.Cancel();
        }
    }

    private static Channel<bool> CreateRefreshSignalChannel() =>
        Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });

    private TelemetrySnapshot BuildSnapshot()
    {
        var snapshot = _reducer.BuildSnapshot(_utcNow());
        return snapshot with { RequestStatus = BuildRequestStatus() };
    }

    private string? BuildRequestStatus()
    {
        if (_activeRequests.Count == 0)
        {
            return null;
        }

        var retrying = _activeRequests.Values.Any(value => value == "RETRYING");
        var phase = retrying ? "RETRYING" : "REQUESTING";
        if (_activeRequests.Count == 1)
        {
            return $"{phase} {_activeRequests.Keys.Single()}…";
        }

        return $"{phase} {_activeRequests.Count} API…";
    }

    private void SetRequestActivity(string endpoint, string? phase)
    {
        lock (_stateGate)
        {
            if (phase is null)
            {
                _activeRequests.Remove(endpoint);
            }
            else
            {
                _activeRequests[endpoint] = phase;
            }

            _snapshots.Writer.TryWrite(BuildSnapshot());
        }
    }

    private string? ReadPersonaName()
    {
        lock (_stateGate)
        {
            return _reducer.PersonaName ?? _options.PersonaName;
        }
    }

    private static bool IsRetryableRestFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        // A temporary API/schema response must never end the long-running map
        // session. Authentication failures are handled before this filter; only
        // genuinely process-fatal failures are allowed to escape.
        return exception is not OutOfMemoryException and
               not AccessViolationException;
    }

    private bool ReadUsesDedicatedMarkers()
    {
        lock (_stateGate)
        {
            return _reducer.UsesDedicatedMarkers;
        }
    }

    private static bool IsRecoverableSocketFailure(Exception exception) =>
        exception is WebSocketException or HttpRequestException or IOException or InvalidDataException or JsonException;

    private static void ValidateOptions(IslePilotOverlayOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.OverlayToken) ||
            options.OverlayToken.Contains('\r') || options.OverlayToken.Contains('\n'))
        {
            throw new ArgumentException("The IslePilot overlay token is invalid.", nameof(options));
        }

        if (options.MeRefreshInterval <= TimeSpan.Zero ||
            options.MapRefreshInterval <= TimeSpan.Zero ||
            options.MarkersRefreshInterval <= TimeSpan.Zero ||
            options.LiveDataLifetime <= TimeSpan.Zero ||
            options.PositionFallbackAfter <= TimeSpan.Zero ||
            options.WebSocketConnectTimeout <= TimeSpan.Zero ||
            options.RestRequestTimeout <= TimeSpan.Zero ||
            options.RestTimeoutRetryDelay < TimeSpan.Zero ||
            options.RestTimeoutRetryCount < 0 ||
            options.AuthenticationRetryDelay < TimeSpan.Zero ||
            options.AuthenticationRetryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Overlay intervals must be positive.");
        }
    }
}
