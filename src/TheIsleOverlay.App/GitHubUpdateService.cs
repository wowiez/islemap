using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace TheIsleOverlay.App;

public interface IAppUpdateBackend
{
    bool CanUpdate { get; }

    string? CurrentVersion { get; }

    Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken);

    Task DownloadUpdateAsync(Action<int>? progress, CancellationToken cancellationToken);

    bool ScheduleApplyAndRestart();
}

public sealed class GitHubUpdateService
{
    public const string RepositoryUrl = "https://github.com/wowiez/islemap";
    public const string UpdateFeedUrl = RepositoryUrl + "/releases/latest/download/";

    private readonly Func<IAppUpdateBackend> _backendFactory;
    private readonly object _checkSync = new();
    private IAppUpdateBackend? _backend;
    private Task<UpdateCheckResult>? _checkTask;
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

    public Task<UpdateCheckResult> CheckForUpdateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<UpdateCheckResult> checkTask;
        lock (_checkSync)
        {
            _checkTask ??= CheckForUpdateCoreAsync(cancellationToken);
            checkTask = _checkTask;
        }

        return checkTask.WaitAsync(cancellationToken);
    }

    private async Task<UpdateCheckResult> CheckForUpdateCoreAsync(
        CancellationToken cancellationToken)
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

            if (!AppVersion.TryParse(_backend.CurrentVersion, out var currentVersion) ||
                !AppVersion.TryParse(_availableVersion, out var availableVersion))
            {
                _availableVersion = null;
                return UpdateCheckResult.Unavailable;
            }

            if (availableVersion <= currentVersion)
            {
                _availableVersion = null;
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
        new SimpleWebSource(
            GitHubUpdateService.UpdateFeedUrl,
            downloader: null,
            timeout: 0.25);

    public bool CanUpdate => _manager.CurrentVersion is not null && !_manager.IsPortable;

    // The executable version is authoritative after an update. Velopack's local
    // package metadata can briefly lag behind on machines that have just applied
    // a release, which previously caused the same release to be offered forever.
    public string? CurrentVersion =>
        typeof(VelopackWebUpdateBackend).Assembly.GetName().Version?.ToString();

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

internal static class AppVersion
{
    public static bool TryParse(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value)) return false;

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var suffix = normalized.IndexOfAny(['-', '+']);
        if (suffix >= 0)
        {
            normalized = normalized[..suffix];
        }

        return Version.TryParse(normalized, out version!);
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
