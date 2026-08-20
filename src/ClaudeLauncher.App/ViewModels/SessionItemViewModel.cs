using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClaudeLauncher.App.ViewModels;

/// <summary>Wraps one <see cref="SessionProfile"/> with its live running state and start/stop commands.
/// Display properties mirror the profile so edits (via <see cref="ApplyProfile"/>) refresh bound UI.
/// The executable and launch arguments are not part of the profile - every session launches with the
/// app-wide default in <see cref="SettingsViewModel"/> unless the user picks a one-off override via
/// <see cref="StartCustomCommand"/>.</summary>
public partial class SessionItemViewModel : ObservableObject
{
    /// <summary>How stale a project's own transcript may be before its activity badge is hidden, for
    /// projects this app didn't launch itself (no <see cref="IsRunning"/> process to confirm liveness
    /// with). A launcher-managed running session always gets a badge regardless of this window.</summary>
    private static readonly TimeSpan UnmanagedFreshnessWindow = TimeSpan.FromMinutes(5);

    /// <summary>How long to wait after an auto-resume relaunch before typing "resume" into the new
    /// console. `claude -c` reopens the previous conversation but sits idle waiting for input - it
    /// doesn't automatically continue the task the usage limit interrupted - so this nudges it back
    /// into action. Best-effort: there's no reliable "the CLI is ready for input" signal to wait on
    /// instead, so this is just long enough for PowerShell and the CLI to finish starting up.</summary>
    private static readonly TimeSpan ResumeNudgeDelay = TimeSpan.FromSeconds(10);

    private readonly ProcessLauncherService _launcher;
    private readonly SettingsViewModel _settings;
    private readonly TranscriptLimitWatcher _limitWatcher = new();
    private Process? _process;

    public SessionProfile Profile { get; private set; }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string workingDirectory;

    [ObservableProperty]
    private string accentColorHex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCustomCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleCustomLaunchMenuCommand))]
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

    [ObservableProperty]
    private ProjectActivityState activityState = ProjectActivityState.Unknown;

    /// <summary>Short excerpt of the most recent conversation turn (last user prompt / last assistant
    /// activity), refreshed alongside <see cref="ActivityState"/> for the dashboard's live preview.</summary>
    [ObservableProperty]
    private string? previewText;

    /// <summary>Whether the "起動" split-button's dropdown (one-off launch options, for this launch
    /// only) is open.</summary>
    [ObservableProperty]
    private bool isCustomLaunchOpen;

    /// <summary>Editable text in the custom-launch dropdown. Pre-filled from the app-wide default each
    /// time the dropdown opens; never persisted anywhere - a one-off override for a single launch.</summary>
    [ObservableProperty]
    private string customLaunchArguments = string.Empty;

    public SessionItemViewModel(SessionProfile profile, ProcessLauncherService launcher, SettingsViewModel settings)
    {
        Profile = profile;
        _launcher = launcher;
        _settings = settings;
        name = profile.Name;
        workingDirectory = profile.WorkingDirectory;
        accentColorHex = profile.AccentColorHex;
        customLaunchArguments = settings.DefaultArguments;
        RefreshScheduleSummary();
        RefreshLimitStatusSummary();
        RefreshActivityState(StatusMarkerStore.ReadFresh(StatusMarkerStore.GetDefaultDirectory(), StatusMarkerStore.DefaultMaxAge, DateTimeOffset.Now));
    }

    public void ApplyProfile(SessionProfile updated)
    {
        Profile = updated;
        Name = updated.Name;
        WorkingDirectory = updated.WorkingDirectory;
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
    /// running and the app-wide <see cref="AppSettings.AutoResumeOnLimitEnabled"/> is on.</summary>
    public bool TryDetectUsageLimit()
    {
        if (!IsRunning || !_settings.AutoResumeOnLimitEnabled)
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
        ScheduleResumeNudge();
        return true;
    }

    /// <summary>Types "resume" into the console <see cref="ResumeNudgeDelay"/> after an auto-resume
    /// relaunch (see <see cref="TryFireAutoResume"/>), via <see cref="ConsoleInputInjector"/>. Guarded
    /// against the session having been stopped or replaced in the meantime by checking both
    /// <see cref="IsRunning"/> and the process ID before sending.</summary>
    private void ScheduleResumeNudge()
    {
        if (_process?.Id is not { } targetPid)
        {
            return;
        }

        var timer = new DispatcherTimer { Interval = ResumeNudgeDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (IsRunning && _process?.Id == targetPid)
            {
                ConsoleInputInjector.TrySendText(targetPid, "resume\r");
            }
        };
        timer.Start();
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

    /// <summary>Called periodically by <see cref="MainViewModel"/>'s schedule timer, for every project
    /// regardless of whether this app launched it (imported/other-terminal projects have no
    /// <see cref="IsRunning"/> process to key off of, so liveness itself has to come from the
    /// transcript). <paramref name="freshMarkers"/> is the full, already-staleness-filtered set of
    /// "awaiting approval" markers for this poll — passed in rather than read here so
    /// <see cref="MainViewModel"/> reads the marker directory once per tick, not once per project.</summary>
    public void RefreshActivityState(IReadOnlyList<StatusMarker> freshMarkers)
    {
        var projectDir = Path.Combine(ClaudeProjectPathResolver.GetProjectsRoot(), ClaudeProjectPathResolver.ToProjectDirName(Profile.WorkingDirectory));
        var file = TranscriptLimitWatcher.PickMostRecentTranscriptFile(projectDir);

        var hasFreshMarker = freshMarkers.Any(m => WorkingDirectoryComparer.AreSame(m.Cwd, Profile.WorkingDirectory));

        if (file is null)
        {
            // No transcript at all - "awaiting approval" still wins if the marker says so (the hook
            // that writes it doesn't depend on the transcript existing), but there's nothing to preview.
            PreviewText = null;
            ActivityState = hasFreshMarker ? ProjectActivityState.AwaitingApproval : ProjectActivityState.Unknown;
            return;
        }

        if (!IsRunning && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow - UnmanagedFreshnessWindow)
        {
            // This app has no process handle for the project, and its transcript hasn't moved
            // recently either — most likely nobody has a `claude` session open on it right now.
            // Showing "待機" (or a stale preview) forever for a project nobody has touched in days
            // would be misleading, so both are cleared rather than left showing the last exchange.
            PreviewText = null;
            ActivityState = hasFreshMarker ? ProjectActivityState.AwaitingApproval : ProjectActivityState.Unknown;
            return;
        }

        // Read once and hand the same text to both the classifier and the preview extractor, instead
        // of two separate tail reads of the same file every poll.
        var text = TranscriptTailFile.ReadTail(file);
        PreviewText = text is null ? null : TranscriptPreviewReader.ExtractPreview(text);

        if (hasFreshMarker)
        {
            // Precedence: awaiting approval always wins, even over a transcript that looks idle (the
            // permission prompt itself isn't written to the transcript, so the two signals can't
            // disagree in a way that should be resolved any other way).
            ActivityState = ProjectActivityState.AwaitingApproval;
            return;
        }

        ActivityState = (text is null ? null : TranscriptActivityClassifier.ClassifyText(text)) ?? ProjectActivityState.Unknown;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => Launch(resume: false);

    /// <summary>Manual equivalent of <see cref="TryFireAutoResume"/>'s relaunch: starts the CLI with
    /// `-c` (continue the most recent conversation in this working directory) instead of a fresh
    /// session, for when the user wants to pick a conversation back up without waiting on schedule or
    /// usage-limit detection.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartResume() => Launch(resume: true);

    /// <summary>Opens/closes the "起動" split-button's dropdown, where the user can launch with
    /// arguments other than the app-wide default for just this one launch.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void ToggleCustomLaunchMenu()
    {
        if (!IsCustomLaunchOpen)
        {
            // Pre-fill from the current default each time it opens, so it's a starting point to tweak
            // rather than whatever was left over (possibly blank) from the last time it was open.
            CustomLaunchArguments = _settings.DefaultArguments;
        }

        IsCustomLaunchOpen = !IsCustomLaunchOpen;
    }

    /// <summary>Launches with <see cref="CustomLaunchArguments"/> instead of the app-wide default -
    /// a one-off override, never written back to <see cref="SettingsViewModel"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartCustom()
    {
        Launch(resume: false, argumentsOverride: CustomLaunchArguments);
        IsCustomLaunchOpen = false;
    }

    private void Launch(bool resume, string? argumentsOverride = null)
    {
        var argumentsText = argumentsOverride ?? _settings.DefaultArguments;
        _process = _launcher.Start(Profile, _settings.DefaultExecutable, argumentsText, resume);
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
