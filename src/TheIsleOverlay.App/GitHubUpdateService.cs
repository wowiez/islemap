using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace TheIsleOverlay.App;

public interface IAppUpdateBackend
{
    bool CanUpdate { get; }

    Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken);

    Task DownloadUpdateAsync(Action<int>? progress, CancellationToken cancellationToken);

    bool ScheduleApplyAndRestart();
}

public sealed class GitHubUpdateService
{
    public const string RepositoryUrl = "https://github.com/wowiez/islemap";
    public const string UpdateFeedUrl = RepositoryUrl + "/releases/latest/download/";

    private readonly Func<IAppUpdateBackend> _backendFactory;
    private IAppUpdateBackend? _backend;
    private string? _availableVersion;
    private bool _ready;

    public GitHubUpdateService()
        : this(() => new VelopackWebUpdateBackend())
    {
    }

    public GitHubUpdateService(Func<IAppUpdateBackend> backendFactory)
    {
        _backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            _backend ??= _backendFactory();
            if (!_backend.CanUpdate)
            {
                return UpdateCheckResult.DevelopmentBuild;
            }

            _availableVersion = await _backend.CheckForUpdateAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(_availableVersion))
            {
                return UpdateCheckResult.Current;
            }

            return new UpdateCheckResult(UpdateCheckState.Available, _availableVersion);
        }
        catch (NotInstalledException)
        {
            return UpdateCheckResult.DevelopmentBuild;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return UpdateCheckResult.Unavailable;
        }
    }

    public async Task<bool> DownloadPendingUpdateAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_backend is null || string.IsNullOrWhiteSpace(_availableVersion))
        {
            return false;
        }

        try
        {
            await _backend.DownloadUpdateAsync(progress, cancellationToken);
            _ready = true;
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public bool ScheduleApplyAndRestart() =>
        _ready && _backend?.ScheduleApplyAndRestart() == true;
}

internal sealed class VelopackWebUpdateBackend : IAppUpdateBackend
{
    private readonly UpdateManager _manager = new(CreateUpdateSource());
    private UpdateInfo? _pendingUpdate;

    internal static IUpdateSource CreateUpdateSource() =>
        new SimpleWebSource(GitHubUpdateService.UpdateFeedUrl);

    public bool CanUpdate => _manager.CurrentVersion is not null && !_manager.IsPortable;

    public async Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _pendingUpdate = await _manager.CheckForUpdatesAsync();
        cancellationToken.ThrowIfCancellationRequested();
        return _pendingUpdate?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadUpdateAsync(Action<int>? progress, CancellationToken cancellationToken)
    {
        if (_pendingUpdate is null)
        {
            throw new InvalidOperationException("No update has been selected for download.");
        }

        return _manager.DownloadUpdatesAsync(_pendingUpdate, progress, cancellationToken);
    }

    public bool ScheduleApplyAndRestart()
    {
        if (_pendingUpdate is null)
        {
            return false;
        }

        _manager.WaitExitThenApplyUpdates(
            _pendingUpdate.TargetFullRelease,
            silent: false,
            restart: true,
            restartArgs: null);
        return true;
    }
}

public enum UpdateCheckState
{
    Current,
    Available,
    DevelopmentBuild,
    Unavailable
}

public sealed record UpdateCheckResult(UpdateCheckState State, string? Version = null)
{
    public static UpdateCheckResult Current { get; } = new(UpdateCheckState.Current);
    public static UpdateCheckResult DevelopmentBuild { get; } = new(UpdateCheckState.DevelopmentBuild);
    public static UpdateCheckResult Unavailable { get; } = new(UpdateCheckState.Unavailable);
}
