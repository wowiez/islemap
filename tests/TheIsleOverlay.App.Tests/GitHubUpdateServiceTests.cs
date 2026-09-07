using System.Net.Http;
using TheIsleOverlay.App;
using Velopack.Sources;

namespace TheIsleOverlay.App.Tests;

public class GitHubUpdateServiceTests
{
    [Fact]
    public void ProductionBackend_UsesDirectReleaseFeedWithoutGitHubApi()
    {
        var source = Assert.IsType<SimpleWebSource>(VelopackWebUpdateBackend.CreateUpdateSource());

        Assert.Equal(
            "https://github.com/wowiez/islemap/releases/latest/download/",
            source.BaseUri.AbsoluteUri);
    }

    [Fact]
    public async Task PortableOrDevelopmentBuild_DoesNotContactGitHub()
    {
        var backend = new FakeBackend { CanUpdate = false };
        var result = await Service(backend).CheckForUpdateAsync();

        Assert.Equal(UpdateCheckState.DevelopmentBuild, result.State);
        Assert.Equal(0, backend.CheckCalls);
        Assert.Equal(0, backend.DownloadCalls);
    }

    [Fact]
    public async Task CurrentVersion_DoesNotDownloadAnything()
    {
        var backend = new FakeBackend { Version = null };
        var service = Service(backend);
        var result = await service.CheckForUpdateAsync();

        Assert.Equal(UpdateCheckState.Current, result.State);
        Assert.Equal(1, backend.CheckCalls);
        Assert.Equal(0, backend.DownloadCalls);
        Assert.False(await service.DownloadPendingUpdateAsync());
        Assert.False(service.ScheduleApplyAndRestart());
    }

    [Fact]
    public async Task RepeatedAndConcurrentChecks_ShareOneBackendRequest()
    {
        var blocker = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new FakeBackend
        {
            Version = "1.7.12",
            CheckBlocker = blocker.Task
        };
        var service = Service(backend);
        var checks = Enumerable.Range(0, 20)
            .Select(_ => service.CheckForUpdateAsync())
            .ToArray();

        await backend.CheckStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, backend.CheckCalls);
        blocker.SetResult();

        var results = await Task.WhenAll(checks);
        Assert.All(results, result =>
            Assert.Equal(new UpdateCheckResult(UpdateCheckState.Available, "1.7.12"), result));
        Assert.Equal(1, backend.CheckCalls);
        Assert.Equal(results[0], await service.CheckForUpdateAsync());
        Assert.Equal(1, backend.CheckCalls);
    }

    [Fact]
    public async Task AvailableUpdate_WaitsForExplicitDownloadAndApplyConsent()
    {
        var backend = new FakeBackend { Version = "1.7.8" };
        var service = Service(backend);
        var progress = new List<int>();

        var result = await service.CheckForUpdateAsync();

        Assert.Equal(new UpdateCheckResult(UpdateCheckState.Available, "1.7.8"), result);
        Assert.Empty(progress);
        Assert.Equal(0, backend.DownloadCalls);
        Assert.Equal(0, backend.ScheduleCalls);
        Assert.False(service.ScheduleApplyAndRestart());

        Assert.True(await service.DownloadPendingUpdateAsync(progress.Add));
        Assert.Equal([25, 100], progress);
        Assert.Equal(1, backend.DownloadCalls);
        Assert.Equal(0, backend.ScheduleCalls);

        Assert.True(service.ScheduleApplyAndRestart());
        Assert.Equal(1, backend.ScheduleCalls);
    }

    [Fact]
    public async Task NetworkFailure_IsNonFatal()
    {
        var backend = new FakeBackend { CheckException = new HttpRequestException("offline") };

        var result = await Service(backend).CheckForUpdateAsync();

        Assert.Equal(UpdateCheckState.Unavailable, result.State);
        Assert.False(Service(backend).ScheduleApplyAndRestart());
    }

    [Fact]
    public async Task DownloadFailure_KeepsUpdatePendingForRetry()
    {
        var backend = new FakeBackend
        {
            Version = "1.7.8",
            DownloadException = new HttpRequestException("download offline")
        };
        var service = Service(backend);
        Assert.Equal(UpdateCheckState.Available, (await service.CheckForUpdateAsync()).State);

        Assert.False(await service.DownloadPendingUpdateAsync());
        Assert.False(service.ScheduleApplyAndRestart());
        Assert.Equal(1, backend.DownloadCalls);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var backend = new FakeBackend { CheckException = new OperationCanceledException(cancellation.Token) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Service(backend).CheckForUpdateAsync(cancellation.Token));
    }

    private static GitHubUpdateService Service(FakeBackend backend) => new(() => backend);

    private sealed class FakeBackend : IAppUpdateBackend
    {
        public bool CanUpdate { get; init; } = true;
        public string? Version { get; init; }
        public Exception? CheckException { get; init; }
        public Exception? DownloadException { get; init; }
        public Task? CheckBlocker { get; init; }
        public TaskCompletionSource CheckStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CheckCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int ScheduleCalls { get; private set; }

        public async Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            CheckCalls++;
            CheckStarted.TrySetResult();
            if (CheckBlocker is not null)
            {
                await CheckBlocker.WaitAsync(cancellationToken);
            }

            if (CheckException is not null)
            {
                throw CheckException;
            }

            return Version;
        }

        public Task DownloadUpdateAsync(Action<int>? progress, CancellationToken cancellationToken)
        {
            DownloadCalls++;
            if (DownloadException is not null)
            {
                return Task.FromException(DownloadException);
            }

            progress?.Invoke(25);
            progress?.Invoke(100);
            return Task.CompletedTask;
        }

        public bool ScheduleApplyAndRestart()
        {
            ScheduleCalls++;
            return true;
        }
    }
}
