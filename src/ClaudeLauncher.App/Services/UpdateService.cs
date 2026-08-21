using Velopack;
using Velopack.Sources;

namespace ClaudeLauncher.App.Services;

/// <summary>Thin wrapper around <see cref="UpdateManager"/> pointed at this repo's GitHub Releases feed.
/// Every call is a no-op when the app isn't running from a Velopack-managed install (e.g. `dotnet run`
/// during development) - <see cref="IsInstalled"/> guards that.</summary>
public sealed class UpdateService
{
    private const string RepoUrl = "https://github.com/eugene-rb/claude-code-launcher";

    private readonly UpdateManager _manager = new(new GithubSource(RepoUrl, null, prerelease: false));

    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersionText => _manager.IsInstalled ? _manager.CurrentVersion?.ToString() ?? "?" : "開発版";

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        if (!IsInstalled)
        {
            return null;
        }

        return await _manager.CheckForUpdatesAsync();
    }

    public Task DownloadUpdatesAsync(UpdateInfo info) =>
        _manager.DownloadUpdatesAsync(info, progress: null, CancellationToken.None);

    /// <summary>Applies the downloaded update and restarts the app. Never returns - the process is
    /// replaced. Callers must dispose any tray/native resources (e.g. the NotifyIcon) before calling this.</summary>
    public void ApplyUpdatesAndRestart(UpdateInfo info) =>
        _manager.ApplyUpdatesAndRestart(info.TargetFullRelease, Array.Empty<string>());
}
