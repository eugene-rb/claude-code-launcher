using System.Windows.Threading;
using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Velopack;

namespace ClaudeLauncher.App.ViewModels;

public enum UpdateState
{
    Idle,
    Checking,
    UpToDate,
    Downloading,
    ReadyToApply,
    Error,
}

/// <summary>Drives the "アップデート" InfoBar on the main window and the version/check-now card in
/// Settings. Owns the periodic background check - first run 3s after startup (so it never delays the
/// window appearing), then every 6h since this is a tray-resident, long-running app.</summary>
public partial class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan InitialCheckDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateService _service;
    private readonly DispatcherTimer _timer;
    private UpdateInfo? _pendingUpdate;

    [ObservableProperty]
    private UpdateState state = UpdateState.Idle;

    [ObservableProperty]
    private string? availableVersion;

    public string CurrentVersionText => _service.CurrentVersionText;

    /// <summary>Invoked right before <see cref="UpdateService.ApplyUpdatesAndRestart"/> replaces the
    /// process, so the caller can dispose native resources (the tray NotifyIcon) first.</summary>
    public Action? BeforeApply { get; set; }

    public UpdateViewModel()
        : this(new UpdateService())
    {
    }

    public UpdateViewModel(UpdateService service)
    {
        _service = service;

        _timer = new DispatcherTimer { Interval = InitialCheckDelay };
        _timer.Tick += async (_, _) =>
        {
            _timer.Stop();
            await CheckAsync();
            _timer.Interval = RecheckInterval;
            _timer.Start();
        };

        if (_service.IsInstalled)
        {
            _timer.Start();
        }
    }

    [RelayCommand]
    private async Task CheckNow() => await CheckAsync();

    private async Task CheckAsync()
    {
        if (!_service.IsInstalled || State is UpdateState.Checking or UpdateState.Downloading)
        {
            return;
        }

        State = UpdateState.Checking;

        try
        {
            var info = await _service.CheckForUpdatesAsync();
            if (info is null)
            {
                State = UpdateState.UpToDate;
                return;
            }

            State = UpdateState.Downloading;
            await _service.DownloadUpdatesAsync(info);

            _pendingUpdate = info;
            AvailableVersion = info.TargetFullRelease.Version.ToString();
            State = UpdateState.ReadyToApply;
        }
        catch
        {
            // Best-effort background feature - a transient network/GitHub failure shouldn't surface
            // as an error dialog. The next scheduled recheck will simply try again.
            State = UpdateState.Error;
        }
    }

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply()
    {
        if (_pendingUpdate is null)
        {
            return;
        }

        BeforeApply?.Invoke();
        _service.ApplyUpdatesAndRestart(_pendingUpdate);
    }

    private bool CanApply() => State == UpdateState.ReadyToApply && _pendingUpdate is not null;

    partial void OnStateChanged(UpdateState value) => ApplyCommand.NotifyCanExecuteChanged();
}
