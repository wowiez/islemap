using System.Runtime.CompilerServices;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Tests;

public sealed class IslePilotRealtimeSessionTests
{
    [Fact]
    public async Task WatchAsync_BootstrapsRestAndPublishesLiveSnapshot()
    {
        var api = new FakeApiClient();
        var socket = new FakeWebSocket(
        [
            new IslePilotOverlayLiveDataDto
            {
                HasDino = true,
                Health = 8,
                Position = new IslePilotOverlayPositionDto { X = 25, Y = 50, Yaw = 90 }
            }
        ]);
        await using var session = CreateSession(api, () => socket);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(
            snapshots,
            value => value.SessionState == TelemetrySessionState.Live &&
                     value.Player?.MapLocation is not null,
            timeout.Token);

        Assert.Equal(1, api.MeCalls);
        Assert.Equal(1, api.MapCalls);
        Assert.Equal("overlay-token", socket.ConnectedToken);
        Assert.Equal("Player", socket.HelloName);
        Assert.Equal(8, snapshot.Player?.ExactVitals?.Health);
        var mapLocation = Assert.IsType<MapPoint>(snapshot.Player?.MapLocation);
        Assert.Equal(0.25, mapLocation.Left, precision: 8);
        Assert.Equal(0.5, mapLocation.Top, precision: 8);
    }

    [Fact]
    public async Task LiveWebSocket_DoesNotSuspendIndependentRestPollers()
    {
        var api = new FakeApiClient { Online = true, Server = "SBTC ISLAND" };
        var socket = new FakeWebSocket(
        [
            new IslePilotOverlayLiveDataDto
            {
                HasDino = true,
                Health = 8,
                Position = new IslePilotOverlayPositionDto { X = 25, Y = 50, Yaw = 90 }
            }
        ]);
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                PersonaName = "Player",
                MeRefreshInterval = TimeSpan.FromMilliseconds(20),
                MapRefreshInterval = TimeSpan.FromMilliseconds(20),
                MarkersRefreshInterval = TimeSpan.FromMilliseconds(20)
            },
            () => socket);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        while ((api.MeCalls < 3 || api.MapCalls < 3 || api.MarkersCalls < 3) &&
               await snapshots.MoveNextAsync().AsTask().WaitAsync(timeout.Token))
        {
        }

        Assert.True(socket.IsConnected);
        Assert.True(api.MeCalls >= 3);
        Assert.True(api.MapCalls >= 3);
        Assert.True(api.MarkersCalls >= 3);
    }

    [Fact]
    public async Task SlowInitialMap_DoesNotDelayWebSocketConnection()
    {
        var api = new BlockingMapApiClient();
        var socket = new FakeWebSocket([]);
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                MeRefreshInterval = TimeSpan.FromHours(1),
                MapRefreshInterval = TimeSpan.FromSeconds(2),
                RestRequestTimeout = TimeSpan.FromSeconds(30),
                RestTimeoutRetryCount = 0
            },
            () => socket);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        Assert.True(await snapshots.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
        await socket.Connected.WaitAsync(timeout.Token);

        Assert.True(api.MapStarted);
        Assert.True(socket.IsConnected);
    }

    [Fact]
    public async Task SlowInitialMe_DoesNotDelayInitialMapRequest()
    {
        var api = new BlockingMeApiClient();
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                MeRefreshInterval = TimeSpan.FromSeconds(5),
                MapRefreshInterval = TimeSpan.FromSeconds(2),
                RestRequestTimeout = TimeSpan.FromSeconds(30),
                RestTimeoutRetryCount = 0
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        Assert.True(await snapshots.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
        await api.MapRequested.WaitAsync(timeout.Token);

        Assert.True(api.MeStarted);
        Assert.Equal(1, api.MapCalls);
    }

    [Fact]
    public async Task SlowInitialMe_DoesNotDelayWebSocketConnection()
    {
        var api = new BlockingMeApiClient();
        var socket = new FakeWebSocket([]);
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                PersonaName = "Cached Player",
                MeRefreshInterval = TimeSpan.FromSeconds(5),
                MapRefreshInterval = TimeSpan.FromSeconds(2),
                RestRequestTimeout = TimeSpan.FromSeconds(30),
                RestTimeoutRetryCount = 0
            },
            () => socket);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        Assert.True(await snapshots.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
        await socket.Connected.WaitAsync(timeout.Token);

        Assert.True(api.MeStarted);
        Assert.True(socket.IsConnected);
        Assert.Equal("Cached Player", socket.HelloName);
    }

    [Fact]
    public async Task RestRequestActivity_IsPublishedWhileEndpointIsInFlight()
    {
        var api = new BlockingMapApiClient();
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                PersonaName = "Player",
                MeRefreshInterval = TimeSpan.FromHours(1),
                MapRefreshInterval = TimeSpan.FromSeconds(2),
                RestRequestTimeout = TimeSpan.FromSeconds(30),
                RestTimeoutRetryCount = 0
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var requesting = await ReadUntilAsync(
            snapshots,
            value => value.RequestStatus?.Contains("/MAP", StringComparison.Ordinal) == true ||
                     value.RequestStatus?.Contains("API", StringComparison.Ordinal) == true,
            timeout.Token);

        Assert.StartsWith("REQUESTING", requesting.RequestStatus);
        Assert.True(api.MapStarted);
    }

    [Fact]
    public async Task SilentAcceptedSocket_RemainsConnectedWithoutReconnectLoop()
    {
        var first = new FakeWebSocket([]);
        var requestedDelays = new List<TimeSpan>();
        await using var session = new IslePilotRealtimeSession(
            new FakeApiClient(),
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                PersonaName = "Player",
                MeRefreshInterval = TimeSpan.FromHours(1),
                MapRefreshInterval = TimeSpan.FromHours(1)
            },
            () => first,
            new IslePilotReconnectBackoff(() => 0.5),
            (delay, _) =>
            {
                requestedDelays.Add(delay);
                return Task.CompletedTask;
            },
            () => DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var connected = await ReadUntilAsync(
            snapshots,
            value => value.SessionState == TelemetrySessionState.Polling,
            timeout.Token);
        timeout.Cancel();

        Assert.Equal(TelemetrySessionState.Polling, connected.SessionState);
        Assert.Empty(requestedDelays);
        Assert.True(first.IsConnected);
    }

    [Fact]
    public async Task SocketFailure_KeepsLatestSnapshotAndReconnects()
    {
        var api = new FakeApiClient();
        var first = new FakeWebSocket(
            [new IslePilotOverlayLiveDataDto { HasDino = true, Health = 8 }],
            new IOException("network lost"));
        var second = new FakeWebSocket([]);
        var sockets = new Queue<FakeWebSocket>([first, second]);
        var requestedDelays = new List<TimeSpan>();
        await using var session = new IslePilotRealtimeSession(
            api,
            Options(),
            () => sockets.Dequeue(),
            new IslePilotReconnectBackoff(() => 0.5),
            (delay, _) =>
            {
                requestedDelays.Add(delay);
                return Task.CompletedTask;
            },
            () => DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var retained = await ReadUntilAsync(
            snapshots,
            value => value.Player?.ExactVitals?.Health == 8,
            timeout.Token);
        await second.Connected.WaitAsync(timeout.Token);

        Assert.Equal(8, retained.Player?.ExactVitals?.Health);
        Assert.Equal([TimeSpan.FromMilliseconds(250)], requestedDelays);
        Assert.True(first.Disposed);
        Assert.Equal("Player", second.HelloName);
    }

    [Fact]
    public async Task SocketFailure_WakesMapAndMarkersPollersForImmediatePositionFallback()
    {
        var api = new FakeApiClient { Online = true, Server = "SBTC ISLAND" };
        var socket = new FakeWebSocket([], connectFailure: new IOException("socket unavailable"));
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                PersonaName = "Player",
                MeRefreshInterval = TimeSpan.FromHours(1),
                MapRefreshInterval = TimeSpan.FromHours(1),
                MarkersRefreshInterval = TimeSpan.FromHours(1)
            },
            () => socket,
            new IslePilotReconnectBackoff(() => 0.5),
            static (_, cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
            () => DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        while ((api.MapCalls < 2 || api.MarkersCalls < 2) &&
               await snapshots.MoveNextAsync().AsTask().WaitAsync(timeout.Token))
        {
        }

        Assert.Equal(2, api.MapCalls);
        Assert.Equal(2, api.MarkersCalls);
    }

    [Fact]
    public async Task LiveSocketThatTurnsSilent_DoesNotReconnectHealthyConnection()
    {
        var first = new FakeWebSocket(
        [
            new IslePilotOverlayLiveDataDto
            {
                HasDino = true,
                Position = new IslePilotOverlayPositionDto { X = 25, Y = 50, Yaw = 90 }
            }
        ]);
        var requestedDelays = new List<TimeSpan>();
        await using var session = new IslePilotRealtimeSession(
            new FakeApiClient(),
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                PersonaName = "Player",
                MeRefreshInterval = TimeSpan.FromHours(1),
                MapRefreshInterval = TimeSpan.FromHours(1)
            },
            () => first,
            new IslePilotReconnectBackoff(() => 0.5),
            (delay, _) =>
            {
                requestedDelays.Add(delay);
                return Task.CompletedTask;
            },
            () => DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var live = await ReadUntilAsync(
            snapshots,
            value => value.SessionState == TelemetrySessionState.Live &&
                     value.Player?.MapLocation is { Left: 0.25, Top: 0.5 },
            timeout.Token);
        await Task.Delay(100, timeout.Token);
        timeout.Cancel();

        Assert.NotNull(live.Player?.ExactMapHeadingDegrees);
        Assert.Empty(requestedDelays);
        Assert.True(first.IsConnected);
    }

    [Fact]
    public async Task TemporaryMapFailures_KeepSessionAliveAndContinueFetching()
    {
        var api = new FakeApiClient();
        api.MapFailures.Enqueue(new InvalidOperationException("temporary schema response"));
        api.MapFailures.Enqueue(new IOException("temporary network failure"));
        api.MapResponse = api.MapResponse with
        {
            Pois =
            [
                new IslePilotOverlayMapPoiDto
                {
                    Id = "delta",
                    Name = "Delta",
                    Shape = "polygon",
                    Points =
                    [
                        new IslePilotOverlayWorldPointDto { X = 10, Y = 10 },
                        new IslePilotOverlayWorldPointDto { X = 20, Y = 10 },
                        new IslePilotOverlayWorldPointDto { X = 20, Y = 20 }
                    ]
                }
            ]
        };
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                MeRefreshInterval = TimeSpan.FromHours(1),
                MapRefreshInterval = TimeSpan.FromMilliseconds(10)
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(
            snapshots,
            value => value.Map?.PointsOfInterest.Count == 1,
            timeout.Token);

        Assert.Equal("Delta", Assert.Single(snapshot.Map!.PointsOfInterest).Name);
        Assert.True(api.MapCalls >= 3);
    }

    [Fact]
    public async Task SbtcMarkers_AreFetchedOnTheirOwnPollerAndOverrideMapMarkers()
    {
        var api = new FakeApiClient
        {
            Online = true,
            Server = "SBTC ISLAND",
            MarkersResponse = new IslePilotOverlayMarkersDto
            {
                Ok = true,
                Markers =
                [
                    new IslePilotOverlayMapMarkerDto
                    {
                        SteamId = "friend",
                        Label = "Fresh SBTC friend",
                        X = 30,
                        Y = 40,
                        Group = true
                    }
                ]
            }
        };
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                MeRefreshInterval = TimeSpan.FromHours(1),
                MapRefreshInterval = TimeSpan.FromHours(1),
                MarkersRefreshInterval = TimeSpan.FromSeconds(5)
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(
            snapshots,
            value => value.Map?.Markers.Any(marker =>
                marker.Label == "Fresh SBTC friend") == true,
            timeout.Token);

        Assert.Equal("Fresh SBTC friend", Assert.Single(snapshot.Map!.Markers).Label);
        Assert.Equal(1, api.MarkersCalls);
        Assert.True(api.MapCalls >= 1);
    }

    [Fact]
    public async Task RestChecks_AreSequentialPerEndpointButRunDifferentTypesInParallel()
    {
        var api = new ConcurrencyTrackingApiClient();
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                MeRefreshInterval = TimeSpan.FromMilliseconds(10),
                MapRefreshInterval = TimeSpan.FromMilliseconds(10),
                RestRequestTimeout = TimeSpan.FromSeconds(1),
                RestTimeoutRetryCount = 0
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        while (api.TotalCalls < 5 && await snapshots.MoveNextAsync())
        {
        }

        Assert.True(api.TotalCalls >= 5);
        Assert.Equal(1, api.MaximumConcurrentMeCalls);
        Assert.Equal(1, api.MaximumConcurrentMapCalls);
        Assert.Equal(2, api.MaximumConcurrentCalls);
    }

    [Fact]
    public async Task BlockedMapRequest_DoesNotStopMeStatusRefreshes()
    {
        var api = new BlockingMapApiClient();
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                MeRefreshInterval = TimeSpan.FromMilliseconds(20),
                MapRefreshInterval = TimeSpan.FromMilliseconds(10),
                RestRequestTimeout = TimeSpan.FromSeconds(30),
                RestTimeoutRetryCount = 0
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        while (api.MeCalls < 3 && await snapshots.MoveNextAsync().AsTask().WaitAsync(timeout.Token))
        {
        }

        Assert.True(api.MapStarted);
        Assert.True(api.MeCalls >= 3);
    }

    [Fact]
    public async Task TimedOutRestCheck_IsRetriedBeforePollingContinues()
    {
        var api = new TimeoutOnceApiClient();
        await using var session = new IslePilotRealtimeSession(
            api,
            new IslePilotOverlayOptions
            {
                OverlayToken = "overlay-token",
                MeRefreshInterval = TimeSpan.FromHours(1),
                MapRefreshInterval = TimeSpan.FromHours(1),
                RestRequestTimeout = TimeSpan.FromMilliseconds(25),
                RestTimeoutRetryDelay = TimeSpan.FromMilliseconds(1),
                RestTimeoutRetryCount = 1
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(
            snapshots,
            value => value.PlayerOnline,
            timeout.Token);

        Assert.True(snapshot.PlayerOnline);
        Assert.Equal(2, api.MeCalls);
    }

    [Fact]
    public async Task RepeatedMeAuthenticationFailure_RequiresLoginOnlyAfterConfirmation()
    {
        var api = new FakeApiClient
        {
            Failure = new IslePilotOverlayAuthenticationException("expired")
        };
        var socketFactoryCalls = 0;
        await using var session = new IslePilotRealtimeSession(
            api,
            Options() with
            {
                AuthenticationRetryDelay = TimeSpan.FromMilliseconds(1),
                AuthenticationRetryCount = 2
            },
            () =>
            {
                socketFactoryCalls++;
                return new FakeWebSocket([]);
            });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(
            snapshots,
            value => value.SessionState == TelemetrySessionState.AuthenticationRequired,
            timeout.Token);

        Assert.Equal("PHIÊN CẦN XÁC THỰC LẠI", snapshot.StatusMessage);
        Assert.Equal(3, api.MeCalls);
        Assert.True(socketFactoryCalls >= 1);
        Assert.False(await snapshots.MoveNextAsync());
    }

    [Fact]
    public async Task TransientMeAuthenticationFailure_RecoversWithoutRequiringLogin()
    {
        var api = new FakeApiClient { Online = true };
        api.MeFailures.Enqueue(new IslePilotOverlayAuthenticationException("temporary"));
        await using var session = new IslePilotRealtimeSession(
            api,
            Options() with
            {
                AuthenticationRetryDelay = TimeSpan.FromMilliseconds(1),
                AuthenticationRetryCount = 2
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(snapshots, value => value.PlayerOnline, timeout.Token);

        Assert.NotEqual(TelemetrySessionState.AuthenticationRequired, snapshot.SessionState);
        Assert.Equal(2, api.MeCalls);
    }

    [Fact]
    public async Task WebSocketAuthenticationFailure_KeepsRestSessionAliveAndReconnects()
    {
        var api = new FakeApiClient { Online = true };
        var socketFactoryCalls = 0;
        var reconnectDelayCalls = 0;
        await using var session = new IslePilotRealtimeSession(
            api,
            Options(),
            () =>
            {
                socketFactoryCalls++;
                return new FakeWebSocket(
                    [],
                    connectFailure: new IslePilotOverlayAuthenticationException("temporary"));
            },
            new IslePilotReconnectBackoff(() => 0.5),
            async (_, cancellationToken) =>
            {
                reconnectDelayCalls++;
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            },
            () => DateTimeOffset.UtcNow);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        var snapshot = await ReadUntilAsync(
            snapshots,
            value => value.PlayerOnline && reconnectDelayCalls > 0,
            timeout.Token);

        Assert.Equal(1, socketFactoryCalls);
        Assert.Equal(1, reconnectDelayCalls);
        Assert.NotEqual(TelemetrySessionState.AuthenticationRequired, snapshot.SessionState);
    }

    [Fact]
    public async Task FeatureEndpointAuthenticationFailures_DoNotEndTheSession()
    {
        var api = new FakeApiClient { Online = true, Server = "SBTC ISLAND" };
        api.MapFailures.Enqueue(new IslePilotOverlayAuthenticationException("map forbidden"));
        api.MarkersFailures.Enqueue(new IslePilotOverlayAuthenticationException("markers forbidden"));
        await using var session = new IslePilotRealtimeSession(
            api,
            Options() with
            {
                MapRefreshInterval = TimeSpan.FromMilliseconds(20),
                MarkersRefreshInterval = TimeSpan.FromMilliseconds(20)
            },
            () => new FakeWebSocket([]));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var snapshots = session.WatchAsync(timeout.Token).GetAsyncEnumerator();

        TelemetrySnapshot? latest = null;
        while ((api.MapCalls < 2 || api.MarkersCalls < 2) &&
               await snapshots.MoveNextAsync().AsTask().WaitAsync(timeout.Token))
        {
            latest = snapshots.Current;
            Assert.NotEqual(TelemetrySessionState.AuthenticationRequired, latest.SessionState);
        }

        Assert.True(api.MapCalls >= 2);
        Assert.True(api.MarkersCalls >= 2);
        Assert.NotNull(latest);
    }

    [Fact]
    public async Task DisposeAsync_DisposesTheOwnedHttpResourceExactlyOnce()
    {
        var ownedResource = new TrackingDisposable();
        var session = new IslePilotRealtimeSession(
            new FakeApiClient(),
            Options(),
            () => new FakeWebSocket([]),
            new IslePilotReconnectBackoff(() => 0.5),
            static (_, _) => Task.CompletedTask,
            static () => DateTimeOffset.UtcNow,
            ownedResource);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, ownedResource.DisposeCalls);
    }

    private static IslePilotRealtimeSession CreateSession(
        IIslePilotOverlayApiClient api,
        Func<IIslePilotOverlayWebSocket> socketFactory) => new(
            api,
            Options(),
            socketFactory);

    private static IslePilotOverlayOptions Options() => new()
    {
        OverlayToken = "overlay-token",
        PersonaName = "Player",
        MeRefreshInterval = TimeSpan.FromHours(1),
        MapRefreshInterval = TimeSpan.FromHours(1)
    };

    private static async Task<TelemetrySnapshot> ReadUntilAsync(
        IAsyncEnumerator<TelemetrySnapshot> snapshots,
        Func<TelemetrySnapshot, bool> predicate,
        CancellationToken cancellationToken)
    {
        while (await snapshots.MoveNextAsync().AsTask().WaitAsync(cancellationToken))
        {
            if (predicate(snapshots.Current))
            {
                return snapshots.Current;
            }
        }

        throw new InvalidOperationException("The telemetry session ended before the expected snapshot.");
    }

    private sealed class FakeApiClient : IIslePilotOverlayApiClient
    {
        public Exception? Failure { get; init; }
        public bool Online { get; init; }
        public string Server { get; init; } = "IslePilot Server";
        public Queue<Exception> MeFailures { get; } = [];
        public Queue<Exception> MapFailures { get; } = [];
        public Queue<Exception> MarkersFailures { get; } = [];
        public IslePilotOverlayMarkersDto MarkersResponse { get; init; } = new();
        public IslePilotOverlayMapDto MapResponse { get; set; } = new()
        {
            Allowed = true,
            Calibration = new IslePilotMapCalibrationDto
            {
                A = new IslePilotMapCalibrationPointDto { WorldX = 0, WorldY = 0, U = 0, V = 0 },
                B = new IslePilotMapCalibrationPointDto { WorldX = 100, WorldY = 100, U = 1, V = 1 }
            }
        };
        public int MeCalls { get; private set; }
        public int MapCalls { get; private set; }
        public int MarkersCalls { get; private set; }

        public Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default)
        {
            MeCalls++;
            if (MeFailures.TryDequeue(out var meFailure))
            {
                return Task.FromException<IslePilotOverlayMeDto>(meFailure);
            }

            if (Failure is not null)
            {
                return Task.FromException<IslePilotOverlayMeDto>(Failure);
            }

            return Task.FromResult(new IslePilotOverlayMeDto
            {
                HasData = true,
                Online = Online,
                SteamId = "76561198000000000",
                PersonaName = "Player",
                Species = "Utahraptor",
                Server = Server,
                MaxHealth = 20
            });
        }

        public Task<IslePilotOverlayMapDto> GetMapAsync(CancellationToken cancellationToken = default)
        {
            MapCalls++;
            if (Failure is not null)
            {
                return Task.FromException<IslePilotOverlayMapDto>(Failure);
            }

            if (MapFailures.TryDequeue(out var mapFailure))
            {
                return Task.FromException<IslePilotOverlayMapDto>(mapFailure);
            }

            return Task.FromResult(MapResponse);
        }

        public Task<IslePilotOverlayMarkersDto> GetMarkersAsync(
            CancellationToken cancellationToken = default)
        {
            MarkersCalls++;
            if (MarkersFailures.TryDequeue(out var markersFailure))
            {
                return Task.FromException<IslePilotOverlayMarkersDto>(markersFailure);
            }

            return Task.FromResult(MarkersResponse);
        }
    }

    private sealed class FakeWebSocket(
        IReadOnlyList<IslePilotOverlayLiveDataDto> frames,
        Exception? failure = null,
        Exception? connectFailure = null) : IIslePilotOverlayWebSocket
    {
        private readonly TaskCompletionSource _connected = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool IsConnected { get; private set; }
        public bool Disposed { get; private set; }
        public string? ConnectedToken { get; private set; }
        public string? HelloName { get; private set; }
        public Task Connected => _connected.Task;

        public Task ConnectAsync(string overlayToken, CancellationToken cancellationToken = default)
        {
            if (connectFailure is not null)
            {
                return Task.FromException(connectFailure);
            }

            ConnectedToken = overlayToken;
            IsConnected = true;
            _connected.TrySetResult();
            return Task.CompletedTask;
        }

        public Task SendHelloAsync(string? personaName, CancellationToken cancellationToken = default)
        {
            HelloName = personaName;
            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<IslePilotOverlayLiveDataDto> ReadLiveAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var frame in frames)
            {
                yield return frame;
            }

            if (failure is not null)
            {
                throw failure;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ConcurrencyTrackingApiClient : IIslePilotOverlayApiClient
    {
        private int _activeCalls;
        private int _activeMeCalls;
        private int _activeMapCalls;
        private int _maximumConcurrentCalls;
        private int _maximumConcurrentMeCalls;
        private int _maximumConcurrentMapCalls;
        private int _totalCalls;

        public int MaximumConcurrentCalls => Volatile.Read(ref _maximumConcurrentCalls);
        public int MaximumConcurrentMeCalls => Volatile.Read(ref _maximumConcurrentMeCalls);
        public int MaximumConcurrentMapCalls => Volatile.Read(ref _maximumConcurrentMapCalls);
        public int TotalCalls => Volatile.Read(ref _totalCalls);

        public async Task<IslePilotOverlayMeDto> GetMeAsync(
            CancellationToken cancellationToken = default)
        {
            await TrackCallAsync(isMap: false, cancellationToken);
            return new IslePilotOverlayMeDto
            {
                HasData = true,
                Online = true,
                PersonaName = "Player",
                Species = "Utahraptor",
                Server = "IslePilot Server"
            };
        }

        public async Task<IslePilotOverlayMapDto> GetMapAsync(
            CancellationToken cancellationToken = default)
        {
            await TrackCallAsync(isMap: true, cancellationToken);
            return new IslePilotOverlayMapDto { Allowed = true };
        }

        private async Task TrackCallAsync(bool isMap, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _totalCalls);
            var active = Interlocked.Increment(ref _activeCalls);
            InterlockedExtensions.Max(ref _maximumConcurrentCalls, active);
            if (isMap)
            {
                var endpointActive = Interlocked.Increment(ref _activeMapCalls);
                InterlockedExtensions.Max(ref _maximumConcurrentMapCalls, endpointActive);
            }
            else
            {
                var endpointActive = Interlocked.Increment(ref _activeMeCalls);
                InterlockedExtensions.Max(ref _maximumConcurrentMeCalls, endpointActive);
            }
            try
            {
                await Task.Delay(30, cancellationToken);
            }
            finally
            {
                if (isMap)
                {
                    Interlocked.Decrement(ref _activeMapCalls);
                }
                else
                {
                    Interlocked.Decrement(ref _activeMeCalls);
                }
                Interlocked.Decrement(ref _activeCalls);
            }
        }
    }

    private sealed class BlockingMapApiClient : IIslePilotOverlayApiClient
    {
        public bool MapStarted { get; private set; }
        public int MeCalls { get; private set; }

        public Task<IslePilotOverlayMeDto> GetMeAsync(CancellationToken cancellationToken = default)
        {
            MeCalls++;
            return Task.FromResult(new IslePilotOverlayMeDto
            {
                HasData = true,
                Online = true,
                PersonaName = "Player",
                Species = "Utahraptor",
                Server = "IslePilot Server"
            });
        }

        public async Task<IslePilotOverlayMapDto> GetMapAsync(
            CancellationToken cancellationToken = default)
        {
            MapStarted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new IslePilotOverlayMapDto();
        }
    }

    private sealed class BlockingMeApiClient : IIslePilotOverlayApiClient
    {
        private readonly TaskCompletionSource _mapRequested = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public bool MeStarted { get; private set; }
        public int MapCalls { get; private set; }
        public Task MapRequested => _mapRequested.Task;

        public async Task<IslePilotOverlayMeDto> GetMeAsync(
            CancellationToken cancellationToken = default)
        {
            MeStarted = true;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new IslePilotOverlayMeDto();
        }

        public Task<IslePilotOverlayMapDto> GetMapAsync(
            CancellationToken cancellationToken = default)
        {
            MapCalls++;
            _mapRequested.TrySetResult();
            return Task.FromResult(new IslePilotOverlayMapDto { Allowed = true });
        }
    }

    private sealed class TimeoutOnceApiClient : IIslePilotOverlayApiClient
    {
        public int MeCalls { get; private set; }

        public async Task<IslePilotOverlayMeDto> GetMeAsync(
            CancellationToken cancellationToken = default)
        {
            MeCalls++;
            if (MeCalls == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return new IslePilotOverlayMeDto
            {
                HasData = true,
                Online = true,
                PersonaName = "Player",
                Species = "Utahraptor",
                Server = "IslePilot Server"
            };
        }

        public Task<IslePilotOverlayMapDto> GetMapAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new IslePilotOverlayMapDto { Allowed = true });
    }

    private static class InterlockedExtensions
    {
        public static void Max(ref int target, int value)
        {
            var current = Volatile.Read(ref target);
            while (current < value)
            {
                var observed = Interlocked.CompareExchange(ref target, value, current);
                if (observed == current)
                {
                    return;
                }

                current = observed;
            }
        }
    }

    private sealed class TrackingDisposable : IDisposable
    {
        public int DisposeCalls { get; private set; }

        public void Dispose() => DisposeCalls++;
    }
}
