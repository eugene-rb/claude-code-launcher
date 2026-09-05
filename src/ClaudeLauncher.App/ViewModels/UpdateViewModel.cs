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

    [ObservableProperty]
    private string? lastErrorMessage;

    public string CurrentVersionText => _service.CurrentVersionText;

    /// <summary>User-visible result of both automatic and manual checks. Previously only
    /// ReadyToApply had UI, so a successful "already current" check and every error looked exactly
    /// like a button that did nothing.</summary>
    public string StatusText => State switch
    {
        UpdateState.Idle when !_service.IsInstalled => "開発版では更新チェックは無効です。",
        UpdateState.Idle => "起動後に自動確認します。",
        UpdateState.Checking => "更新を確認しています…",
        UpdateState.UpToDate => "最新版です。",
        UpdateState.Downloading => "更新をダウンロードしています…",
        UpdateState.ReadyToApply => $"v{AvailableVersion} をダウンロード済みです。再起動して適用できます。",
        UpdateState.Error => $"更新確認に失敗しました{(string.IsNullOrWhiteSpace(LastErrorMessage) ? "。" : $": {LastErrorMessage}")}",
        _ => string.Empty,
    };

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
        LastErrorMessage = null;

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
        catch (Exception ex)
        {
            // Best-effort background feature - a transient network/GitHub failure shouldn't surface
            // as an error dialog. The next scheduled recheck will simply try again.
            LastErrorMessage = ex.Message;
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

    partial void OnStateChanged(UpdateState value)
    {
        ApplyCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(StatusText));
    }

    partial void OnAvailableVersionChanged(string? value) => OnPropertyChanged(nameof(StatusText));

    partial void OnLastErrorMessageChanged(string? value) => OnPropertyChanged(nameof(StatusText));
}
