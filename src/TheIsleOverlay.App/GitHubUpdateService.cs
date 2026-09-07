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

    private readonly Func<IAppUpdateBackend> _backendFactory;
    private IAppUpdateBackend? _backend;
    private bool _ready;

    public GitHubUpdateService()
        : this(() => new VelopackGitHubUpdateBackend())
    {
    }

    public GitHubUpdateService(Func<IAppUpdateBackend> backendFactory)
    {
        _backendFactory = backendFactory ?? throw new ArgumentNullException(nameof(backendFactory));
    }

    public async Task<UpdatePreparationResult> PrepareUpdateAsync(
        Action<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _backend ??= _backendFactory();
            if (!_backend.CanUpdate)
            {
                return UpdatePreparationResult.DevelopmentBuild;
            }

            var version = await _backend.CheckForUpdateAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(version))
            {
                return UpdatePreparationResult.Current;
            }

            await _backend.DownloadUpdateAsync(progress, cancellationToken);
            _ready = true;
            return new UpdatePreparationResult(UpdatePreparationState.Ready, version);
        }
        catch (NotInstalledException)
        {
            return UpdatePreparationResult.DevelopmentBuild;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return UpdatePreparationResult.Unavailable;
        }
    }

    public bool ScheduleApplyAndRestart() =>
        _ready && _backend?.ScheduleApplyAndRestart() == true;
}

internal sealed class VelopackGitHubUpdateBackend : IAppUpdateBackend
{
    private readonly UpdateManager _manager = new(
        new GithubSource(GitHubUpdateService.RepositoryUrl, accessToken: null, prerelease: false));
    private UpdateInfo? _pendingUpdate;

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

public enum UpdatePreparationState
{
    Current,
    Ready,
    DevelopmentBuild,
    Unavailable
}

public sealed record UpdatePreparationResult(UpdatePreparationState State, string? Version = null)
{
    public static UpdatePreparationResult Current { get; } = new(UpdatePreparationState.Current);
    public static UpdatePreparationResult DevelopmentBuild { get; } = new(UpdatePreparationState.DevelopmentBuild);
    public static UpdatePreparationResult Unavailable { get; } = new(UpdatePreparationState.Unavailable);
}
