using System.Diagnostics;
using System.Globalization;
using System.Windows;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeLauncher.App.ViewModels;

/// <summary>Wraps one <see cref="SessionProfile"/> with its live running state and start/stop commands.
/// Display properties mirror the profile so edits (via <see cref="ApplyProfile"/>) refresh bound UI.</summary>
public partial class SessionItemViewModel : ObservableObject
{
    private readonly ProcessLauncherService _launcher;
    private readonly TranscriptLimitWatcher _limitWatcher = new();
    private Process? _process;

    public SessionProfile Profile { get; private set; }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string workingDirectory;

    [ObservableProperty]
    private string executable;

    [ObservableProperty]
    private string arguments;

    [ObservableProperty]
    private string accentColorHex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool isRunning;

    [ObservableProperty]
    private int? processId;

    [ObservableProperty]
    private string? scheduleSummary;

    [ObservableProperty]
    private string? limitStatusSummary;

    [ObservableProperty]
    private string manualLimitTimeText = string.Empty;

    public SessionItemViewModel(SessionProfile profile, ProcessLauncherService launcher)
    {
        Profile = profile;
        _launcher = launcher;
        name = profile.Name;
        workingDirectory = profile.WorkingDirectory;
        executable = profile.Executable;
        arguments = profile.Arguments;
        accentColorHex = profile.AccentColorHex;
        RefreshScheduleSummary();
        RefreshLimitStatusSummary();
    }

    public void ApplyProfile(SessionProfile updated)
    {
        Profile = updated;
        Name = updated.Name;
        WorkingDirectory = updated.WorkingDirectory;
        Executable = updated.Executable;
        Arguments = updated.Arguments;
        AccentColorHex = updated.AccentColorHex;
        RefreshScheduleSummary();
        RefreshLimitStatusSummary();
    }

    /// <summary>Called periodically by <see cref="MainViewModel"/>'s schedule timer. Returns true if
    /// this session's persisted state changed (a launch fired, or a failed attempt was marked
    /// handled) so the caller knows to persist sessions to disk.</summary>
    public bool TryFireScheduledLaunch(DateTimeOffset now)
    {
        if (!ScheduleEvaluator.ShouldFire(Profile, IsRunning, now) || !StartCommand.CanExecute(null))
        {
            return false;
        }

        try
        {
            StartCommand.Execute(null);
        }
        catch (Exception)
        {
            // Unattended path: never retry-storm on failure (e.g. the working directory was
            // deleted after the schedule was configured). Mark the schedule as handled instead.
            if (Profile.Repeat == ScheduleRepeat.Once)
            {
                Profile.ScheduleEnabled = false;
            }
            else
            {
                Profile.LastLaunchedAt = now;
            }

            ScheduleSummary = "予約起動に失敗しました";
            return true;
        }

        if (Profile.Repeat == ScheduleRepeat.Once)
        {
            Profile.ScheduleEnabled = false;
        }

        RefreshScheduleSummary();
        return true;
    }

    /// <summary>Called periodically by <see cref="MainViewModel"/>'s schedule timer. Polls this
    /// session's own Claude Code transcript for a newly-appeared usage-limit event; on a match,
    /// schedules an auto-resume 5 minutes after the parsed reset time. No-op unless the session is
    /// running and <see cref="SessionProfile.LimitDetectionEnabled"/> is on.</summary>
    public bool TryDetectUsageLimit()
    {
        if (!IsRunning || !Profile.LimitDetectionEnabled)
        {
            return false;
        }

        var resetAt = _limitWatcher.Poll(Profile.WorkingDirectory);
        if (resetAt is not { } at)
        {
            return false;
        }

        Profile.AutoResumeAt = at + TimeSpan.FromMinutes(5);
        RefreshLimitStatusSummary();
        return true;
    }

    /// <summary>Called periodically by <see cref="MainViewModel"/>'s schedule timer. If an auto-resume
    /// is due, stops the still-blocked process (if it's still running) and relaunches with `-c`.
    /// Stopping happens here, at fire time, rather than at detection time, so a window the user might
    /// still be reading isn't killed the moment the limit message appears. A resume that's gone stale
    /// (the app was closed for hours past the reset time) is cancelled instead of fired, so reopening
    /// the app days later doesn't silently relaunch a session the user has moved on from.</summary>
    public bool TryFireAutoResume(DateTimeOffset now)
    {
        if (ScheduleEvaluator.IsAutoResumeStale(Profile, now))
        {
            Profile.AutoResumeAt = null;
            LimitStatusSummary = "自動再開の予定時刻を過ぎたため取り消されました";
            return true;
        }

        if (!ScheduleEvaluator.ShouldAutoResume(Profile, now))
        {
            return false;
        }

        if (IsRunning)
        {
            Stop();
        }

        try
        {
            Launch(resume: true);
        }
        catch (Exception)
        {
            // Unattended path: never retry-storm on failure.
            Profile.AutoResumeAt = null;
            LimitStatusSummary = "自動再開に失敗しました";
            return true;
        }

        Profile.AutoResumeAt = null;
        RefreshLimitStatusSummary();
        return true;
    }

    /// <summary>Manual fallback for when auto-detection doesn't fire (e.g. the reset-time wording
    /// changes in a future CLI version): the user types the reset time they see on screen (H:mm,
    /// today or tomorrow if already past) and the app schedules the same 5-minutes-later resume.</summary>
    [RelayCommand]
    private void RecordManualLimit()
    {
        if (!TimeSpan.TryParseExact(ManualLimitTimeText.Trim(), ["h\\:mm", "hh\\:mm"], CultureInfo.InvariantCulture, out var time))
        {
            LimitStatusSummary = "時刻は H:mm 形式(例: 15:30)で入力してください";
            return;
        }

        var now = DateTimeOffset.Now;
        var candidate = new DateTimeOffset(now.Date + time, now.Offset);
        if (candidate < now)
        {
            candidate = candidate.AddDays(1);
        }

        Profile.AutoResumeAt = candidate + TimeSpan.FromMinutes(5);
        ManualLimitTimeText = string.Empty;
        RefreshLimitStatusSummary();
    }

    /// <summary>Recomputes the usage-limit-detection badge from <see cref="SessionProfile.AutoResumeAt"/>.
    /// Public so <see cref="MainViewModel"/>'s periodic timer can refresh it even on ticks where
    /// nothing fired.</summary>
    public void RefreshLimitStatusSummary()
    {
        LimitStatusSummary = Profile.AutoResumeAt is { } at
            ? $"制限検知 → {at.LocalDateTime:yyyy/MM/dd HH:mm} に自動再開予定 (--continue)"
            : null;
    }

    /// <summary>Recomputes the human-readable schedule badge. Public so <see cref="MainViewModel"/>'s
    /// periodic timer can refresh it even on ticks where nothing fired — a "Once" schedule whose
    /// grace window has already elapsed (e.g. the app was closed through it) must stop claiming
    /// 起動予定 since <see cref="ScheduleEvaluator.ShouldFire"/> will never fire it.</summary>
    public void RefreshScheduleSummary()
    {
        ScheduleSummary = Profile switch
        {
            { ScheduleEnabled: false } => null,
            { Repeat: ScheduleRepeat.Once, ScheduledAt: { } at } when at + ScheduleEvaluator.GraceWindow < DateTimeOffset.Now
                => $"{at.LocalDateTime:yyyy/MM/dd HH:mm} の予約は実行されませんでした",
            { Repeat: ScheduleRepeat.Once, ScheduledAt: { } at } => $"{at.LocalDateTime:yyyy/MM/dd HH:mm} に起動予定",
            { Repeat: ScheduleRepeat.Daily, DailyTime: { } time } => $"毎日 {time:hh\\:mm} に起動",
            _ => null,
        };
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => Launch(resume: false);

    private void Launch(bool resume)
    {
        _process = _launcher.Start(Profile, resume);
        _process.Exited += OnProcessExited;
        ProcessId = _process.Id;
        IsRunning = true;
        Profile.LastLaunchedAt = DateTimeOffset.Now;
        _limitWatcher.Reset(DateTimeOffset.Now);
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        var proc = _process;
        if (proc is null)
        {
            return;
        }

        proc.Exited -= OnProcessExited;
        _process = null;
        IsRunning = false;
        ProcessId = null;

        _launcher.Stop(proc);
        proc.Dispose();
    }

    private bool CanStop() => IsRunning;

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process proc)
        {
            return;
        }

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (!ReferenceEquals(_process, proc))
            {
                // Already handled by an explicit Stop() call; avoid double-disposing/overwriting state.
                return;
            }

            _process = null;
            IsRunning = false;
            ProcessId = null;
            proc.Dispose();
        });
    }
}
