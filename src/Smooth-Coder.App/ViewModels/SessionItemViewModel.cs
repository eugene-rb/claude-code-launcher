using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using SmoothCoder.App.Models;
using SmoothCoder.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SmoothCoder.App.ViewModels;

/// <summary>Wraps one <see cref="SessionProfile"/> with its live running state and start/stop commands.
/// Display properties mirror the profile so edits (via <see cref="ApplyProfile"/>) refresh bound UI.
/// The executable and launch arguments are not part of the profile - every session launches with the
/// per-agent default in <see cref="SettingsViewModel"/> for <see cref="SessionProfile.AgentKind"/>
/// unless the user picks a one-off AI/arguments override via <see cref="StartCustomCommand"/>. That
/// override leaves the configured default alone, but is remembered as the most recently used AI so a
/// later <see cref="StartResumeCommand"/> can reopen the correct conversation.</summary>
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
    private readonly AgentCooldownStore _cooldowns;
    private readonly VoiceNotificationService _voice;
    private readonly SharedTaskContextService _sharedContext = new();
    private TranscriptLimitWatcher _limitWatcher = new(AgentTranscriptSourceRegistry.Get(AgentKind.ClaudeCode));
    private AgentKind _activeAgentKind;
    private Process? _process;

    /// <summary>Timestamp of the last end-of-turn record seen in this project's transcript, so the
    /// announcement fires once per turn rather than on every two-second poll that still sees the same
    /// record. Null until the first one is seen - and that first sighting only seeds this, because the
    /// record it finds is whatever the project happened to be left in, not something that just
    /// happened. Moving backwards (a different session file becoming the newest for this project)
    /// re-seeds for the same reason. Only Codex CLI reaches this; Claude Code's end-of-turn arrives as
    /// a hook marker and is announced by <see cref="AgentEventNotifier"/>.</summary>
    private DateTimeOffset? _lastTurnCompleteAt;

    public SessionProfile Profile { get; private set; }

    public event EventHandler? ProfileChanged;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string workingDirectory;

    [ObservableProperty]
    private string accentColorHex;

    /// <summary>Display name of <see cref="SessionProfile.AgentKind"/> (see <see cref="AgentCatalog"/>),
    /// shown as a badge on the dashboard card.</summary>
    [ObservableProperty]
    private string agentDisplayName;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartResumeCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartHandoffCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartCustomCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    private bool isRunning;

    [ObservableProperty]
    private int? processId;

    [ObservableProperty]
    private string? scheduleSummary;

    [ObservableProperty]
    private string? limitStatusSummary;

    [ObservableProperty]
    private ProjectActivityState activityState = ProjectActivityState.Unknown;

    /// <summary>Short excerpt of the most recent conversation turn (last user prompt / last assistant
    /// activity), refreshed alongside <see cref="ActivityState"/> for the dashboard's live preview.</summary>
    [ObservableProperty]
    private string? previewText;

    /// <summary>Whether the "起動" split-button's dropdown (one-off launch options, for this launch
    /// only) is open. Two-way bound to <c>ui:SplitButton.IsDropDownOpen</c>, which the control itself
    /// flips when its toggle part is clicked - <see cref="OnIsCustomLaunchOpenChanged"/> reacts to that
    /// rather than a command driving it.</summary>
    [ObservableProperty]
    private bool isCustomLaunchOpen;

    /// <summary>Editable text in the custom-launch dropdown. Pre-filled from the selected AI's default
    /// each time the dropdown opens (or its AI selection changes); never persisted anywhere - a one-off
    /// override for a single launch.</summary>
    [ObservableProperty]
    private string customLaunchArguments = string.Empty;

    /// <summary>AI selected in the custom-launch dropdown. Pre-filled from <see cref="Profile"/>'s own
    /// <see cref="SessionProfile.AgentKind"/> each time the dropdown opens; never persisted anywhere -
    /// a one-off override for a single launch. The project's own <see cref="SessionProfile.AgentKind"/>
    /// (set via the project edit dialog) is what the plain "起動"/"続きから" buttons always use.</summary>
    [ObservableProperty]
    private AgentKind customLaunchAgentKind;

    /// <summary>The four AI options offered by the custom-launch dropdown and the project edit dialog.</summary>
    public IReadOnlyList<AgentDefinition> AgentOptions => AgentCatalog.All;

    partial void OnIsCustomLaunchOpenChanged(bool value)
    {
        if (value)
        {
            // Pre-fill from the project's own AI/arguments each time it opens, so it's a starting
            // point to tweak rather than whatever was left over from the last time it was open.
            CustomLaunchAgentKind = Profile.AgentKind;
            CustomLaunchArguments = _settings.GetArguments(Profile.AgentKind);
        }
    }

    partial void OnCustomLaunchAgentKindChanged(AgentKind value) => CustomLaunchArguments = _settings.GetArguments(value);

    public SessionItemViewModel(
        SessionProfile profile,
        ProcessLauncherService launcher,
        SettingsViewModel settings,
        AgentCooldownStore cooldowns,
        VoiceNotificationService voice)
    {
        Profile = profile;
        _launcher = launcher;
        _settings = settings;
        _cooldowns = cooldowns;
        _voice = voice;
        name = profile.Name;
        workingDirectory = profile.WorkingDirectory;
        accentColorHex = profile.AccentColorHex;
        agentDisplayName = AgentCatalog.Get(profile.AgentKind).DisplayName;
        customLaunchAgentKind = profile.AgentKind;
        customLaunchArguments = settings.GetArguments(profile.AgentKind);
        _activeAgentKind = profile.LastUsedAgentKind ?? profile.AgentKind;
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
        AgentDisplayName = AgentCatalog.Get(updated.AgentKind).DisplayName;
        StartHandoffCommand.NotifyCanExecuteChanged();
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
    /// session's own transcript for a newly-appeared usage-limit event; on a match, schedules an
    /// auto-resume 5 minutes after the parsed reset time. No-op unless the session is running, the
    /// app-wide <see cref="AppSettings.AutoResumeOnLimitEnabled"/> is on, and
    /// <see cref="IAgentTranscriptSource.SupportsUsageLimitAutoResume"/> is true for this project's
    /// agent - currently Claude Code and Codex CLI (see <see cref="AgentCatalog"/>).</summary>
    public bool TryDetectUsageLimit()
    {
        if (!IsRunning || !_settings.AutoResumeOnLimitEnabled
            || !AgentTranscriptSourceRegistry.Get(_activeAgentKind).SupportsUsageLimitAutoResume)
        {
            return false;
        }

        var resetAt = _limitWatcher.Poll(Profile.WorkingDirectory);
        if (resetAt is not { } at)
        {
            return false;
        }

        var now = DateTimeOffset.Now;

        // Check the reported reset before Plan turns it into a fresh handoff deadline.
        if (!ScheduleEvaluator.ShouldArmAutoResume(at + HandoffPlanner.ResumeGrace, now))
            return false;

        // Record the limit account-wide before deciding anything: every other project's failover, and
        // this one's next hop back, depends on knowing this agent is spent.
        _cooldowns.Set(_activeAgentKind, at, now);

        var counterpart = _settings.CrossAgentHandoffEnabled
            ? SharedTaskContextService.GetCounterpart(_activeAgentKind)
            : null;

        var plan = HandoffPlanner.Plan(
            _activeAgentKind,
            _cooldowns.Get(_activeAgentKind, now) ?? at,
            counterpart,
            counterpart is { } other ? _cooldowns.Get(other, now) : null,
            Profile.LastHandoffAt,
            now);

        if (!ScheduleEvaluator.ShouldArmAutoResume(plan.FireAt, now))
        {
            // Same reset time as an auto-resume already fired for this session - see
            // ScheduleEvaluator.ShouldArmAutoResume for why this must not re-arm.
            return false;
        }

        Profile.AutoResumeAt = plan.FireAt;
        Profile.AutoResumeAgentKind = plan.TargetAgent == _activeAgentKind ? null : plan.TargetAgent;
        Profile.AutoResumeIsWaitingForReset = plan.Action == FailoverAction.WaitForReset;
        RefreshLimitStatusSummary();
        _voice.Play(plan.Action == FailoverAction.WaitForReset ? VoiceCue.WaitingForReset : VoiceCue.LimitDetected);
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
        if (!_settings.AutoResumeOnLimitEnabled && Profile.AutoResumeAt is not null)
        {
            ClearAutoResume();
            RefreshLimitStatusSummary();
            return true;
        }

        if (ScheduleEvaluator.IsAutoResumeStale(Profile, now))
        {
            ClearAutoResume();
            LimitStatusSummary = "自動再開の予定時刻を過ぎたため取り消されました";
            return true;
        }

        if (!ScheduleEvaluator.ShouldAutoResume(Profile, now))
        {
            return false;
        }

        var sourceAgent = _activeAgentKind;
        var targetAgent = Profile.AutoResumeAgentKind ?? sourceAgent;
        if (!_settings.CrossAgentHandoffEnabled)
            targetAgent = sourceAgent;

        var revised = HandoffPlanner.Revalidate(sourceAgent, targetAgent,
            _cooldowns.Get(sourceAgent, now), _cooldowns.Get(targetAgent, now),
            Profile.LastHandoffAt, now);
        if (revised is { } plan)
        {
            targetAgent = plan.TargetAgent;
            Profile.AutoResumeAt = plan.FireAt;
            Profile.AutoResumeAgentKind = targetAgent == sourceAgent ? null : targetAgent;
            Profile.AutoResumeIsWaitingForReset = plan.Action == FailoverAction.WaitForReset;
            if (plan.FireAt > now)
            {
                RefreshLimitStatusSummary();
                return true;
            }
        }
        var isHandoff = targetAgent != sourceAgent;
        try
        {
            string? continuationPrompt = null;
            if (isHandoff)
            {
                var checkpoint = _sharedContext.Capture(Profile.WorkingDirectory, sourceAgent);
                continuationPrompt = SharedTaskContextService.BuildContinuationPrompt(checkpoint, sourceAgent);
            }

            if (IsRunning)
            {
                Stop();
            }

            if (isHandoff)
            {
                Launch(resume: false, agentKindOverride: targetAgent, initialPrompt: continuationPrompt);
                Profile.AgentKind = targetAgent;
                Profile.LastHandoffAt = now;
                AgentDisplayName = AgentCatalog.Get(targetAgent).DisplayName;
            }
            else
            {
                // Resume with the continuation instruction as the CLI's positional prompt, which both
                // `claude -c "…"` and `codex resume --last "…"` accept, so the resumed session picks the
                // task back up on its own. CompactFirst is the exception: its `/compact` takes that one
                // positional slot (see ProcessLauncherService.BuildLaunchArguments), so that mode still
                // relies on the typed nudge below.
                var useCompactSlot = _settings.ResumeMode == ResumeMode.CompactFirst
                    && AgentCatalog.Get(targetAgent).CompactResumeExtraToken is not null;
                Launch(
                    resume: true,
                    agentKindOverride: targetAgent,
                    initialPrompt: useCompactSlot ? null : SharedTaskContextService.ResumeContinuationPrompt);

                if (useCompactSlot)
                {
                    ScheduleResumeNudge();
                }
            }
        }
        catch (Exception)
        {
            // Unattended path: never retry-storm on failure.
            ClearAutoResume();
            LimitStatusSummary = "自動再開に失敗しました";
            _voice.Play(VoiceCue.ResumeFailed);
            return true;
        }

        ClearAutoResume();
        RefreshLimitStatusSummary();
        _voice.Play(isHandoff
            ? VoiceNotificationService.HandoffCueFor(targetAgent) ?? VoiceCue.AutoResume
            : VoiceCue.AutoResume);
        return true;
    }

    private void ClearAutoResume()
    {
        Profile.AutoResumeAt = null;
        Profile.AutoResumeAgentKind = null;
        Profile.AutoResumeIsWaitingForReset = false;
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

    /// <summary>Recomputes the usage-limit-detection badge from <see cref="SessionProfile.AutoResumeAt"/>.
    /// Public so <see cref="MainViewModel"/>'s periodic timer can refresh it even on ticks where
    /// nothing fired.</summary>
    public void RefreshLimitStatusSummary()
    {
        LimitStatusSummary = (Profile.AutoResumeAt, Profile.AutoResumeAgentKind, Profile.AutoResumeIsWaitingForReset) switch
        {
            (null, _, _) => null,
            // Both accounts are spent, so the badge has to say what is being waited on - otherwise a
            // park that is hours or days out is indistinguishable from a handoff that silently failed.
            ({ } at, { } target, true) => $"両方が利用上限 → {at.LocalDateTime:MM/dd HH:mm} に {AgentCatalog.Get(target).DisplayName} で再開予定",
            ({ } at, null, true) => $"両方が利用上限 → {at.LocalDateTime:MM/dd HH:mm} に再開予定",
            ({ } at, { } target, false) => $"制限検知 → {AgentCatalog.Get(target).DisplayName} へ引き継ぎ予定 ({at.LocalDateTime:HH:mm:ss})",
            ({ } at, null, false) => $"制限検知 → {at.LocalDateTime:yyyy/MM/dd HH:mm} に自動再開予定",
        };
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
    /// <see cref="MainViewModel"/> reads the marker directory once per tick, not once per project.
    /// Announcing those markers is <see cref="AgentEventNotifier"/>'s job, not this method's - they are
    /// read here only for the badge.</summary>
    public void RefreshActivityState(IReadOnlyList<StatusMarker> freshMarkers)
    {
        var sourceKind = IsRunning ? _activeAgentKind : Profile.AgentKind;
        var source = AgentTranscriptSourceRegistry.Get(sourceKind);
        var file = source.FindMostRecentTranscriptFile(Profile.WorkingDirectory);

        // The hook that writes markers is Claude Code-only, so they're only ever meaningful for a
        // Claude Code project. Only the two "blocked on the user" reasons belong on the badge: the
        // hook also writes a marker when a turn simply ends, and that is idle, not awaiting anything.
        var isBlockedOnUser = sourceKind == AgentKind.ClaudeCode
            && freshMarkers.Any(m => m.IsBlockedOnUser && WorkingDirectoryComparer.AreSame(m.Cwd, Profile.WorkingDirectory));

        if (file is null)
        {
            // No transcript at all - "awaiting approval" still wins if the marker says so (the hook
            // that writes it doesn't depend on the transcript existing), but there's nothing to preview.
            PreviewText = null;
            ActivityState = isBlockedOnUser ? ProjectActivityState.AwaitingApproval : ProjectActivityState.Unknown;
            return;
        }

        if (!IsRunning && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow - UnmanagedFreshnessWindow)
        {
            // This app has no process handle for the project, and its transcript hasn't moved
            // recently either — most likely nobody has a `claude` session open on it right now.
            // Showing "待機" (or a stale preview) forever for a project nobody has touched in days
            // would be misleading, so both are cleared rather than left showing the last exchange.
            PreviewText = null;
            ActivityState = isBlockedOnUser ? ProjectActivityState.AwaitingApproval : ProjectActivityState.Unknown;
            return;
        }

        // Read once and hand the same text to the classifier, the preview extractor and the
        // end-of-turn detector, instead of three separate tail reads of the same file every poll.
        var text = TranscriptTailFile.ReadTail(file);
        PreviewText = text is null ? null : source.ExtractPreview(text);

        if (text is not null)
        {
            AnnounceTurnComplete(source.TryDetectTurnComplete(text));
        }

        if (isBlockedOnUser)
        {
            // Precedence: awaiting approval always wins, even over a transcript that looks idle (the
            // permission prompt itself isn't written to the transcript, so the two signals can't
            // disagree in a way that should be resolved any other way).
            ActivityState = ProjectActivityState.AwaitingApproval;
            return;
        }

        ActivityState = (text is null ? null : source.Classify(text)) ?? ProjectActivityState.Unknown;
    }

    /// <summary>Speaks the end-of-turn cue when <paramref name="completedAt"/> is a turn that finished
    /// since the last poll. See <see cref="_lastTurnCompleteAt"/> for why the first sighting - and a
    /// timestamp that moves backwards - only re-seed instead of announcing.</summary>
    private void AnnounceTurnComplete(DateTimeOffset? completedAt)
    {
        if (completedAt is not { } at)
        {
            return;
        }

        var previous = _lastTurnCompleteAt;
        _lastTurnCompleteAt = at;

        if (previous is { } last && at > last)
        {
            _voice.Play(VoiceCue.TurnComplete);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start() => Launch(resume: false);

    /// <summary>Manual equivalent of <see cref="TryFireAutoResume"/>'s relaunch: starts the CLI with
    /// `-c` (continue the most recent conversation in this working directory) instead of a fresh
    /// session, for when the user wants to pick a conversation back up without waiting on schedule or
    /// usage-limit detection.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartResume() => Launch(resume: true, agentKindOverride: Profile.LastUsedAgentKind);

    /// <summary>Manually moves the latest Claude/Codex task to the other CLI through the same shared
    /// checkpoint used by automatic limit failover.</summary>
    [RelayCommand(CanExecute = nameof(CanHandoff))]
    private void StartHandoff()
    {
        var sourceAgent = IsRunning ? _activeAgentKind : Profile.AgentKind;
        if (SharedTaskContextService.GetCounterpart(sourceAgent) is not { } targetAgent)
        {
            return;
        }

        // Capture before stopping, for the same reason the automatic path does: Stop kills the process
        // tree, and a CLI killed mid-write can lose the tail of the transcript being captured.
        var checkpoint = _sharedContext.Capture(Profile.WorkingDirectory, sourceAgent);
        var prompt = SharedTaskContextService.BuildContinuationPrompt(checkpoint, sourceAgent);

        if (IsRunning)
        {
            Stop();
        }

        Launch(resume: false, agentKindOverride: targetAgent, initialPrompt: prompt);
        Profile.AgentKind = targetAgent;
        Profile.LastHandoffAt = DateTimeOffset.Now;
        AgentDisplayName = AgentCatalog.Get(targetAgent).DisplayName;
        _voice.Play(VoiceNotificationService.HandoffCueFor(targetAgent) ?? VoiceCue.AutoResume);
        ProfileChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Unlike the other launch commands, this one is available while the session is running:
    /// handing a live task to the other CLI is the point, and <see cref="StartHandoff"/> stops the
    /// current process itself once it has captured the checkpoint.</summary>
    private bool CanHandoff() =>
        SharedTaskContextService.GetCounterpart(IsRunning ? _activeAgentKind : Profile.AgentKind) is not null;

    /// <summary>Launches with <see cref="CustomLaunchAgentKind"/>/<see cref="CustomLaunchArguments"/>
    /// instead of the project's own AI/default arguments - a one-off override which never changes
    /// the project's configured AI or settings. The selected AI is still recorded as the last used
    /// one, so "continue" can resume the conversation it created.</summary>
    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartCustom()
    {
        Launch(resume: false, argumentsOverride: CustomLaunchArguments, agentKindOverride: CustomLaunchAgentKind);
        IsCustomLaunchOpen = false;
    }

    private void Launch(
        bool resume,
        string? argumentsOverride = null,
        AgentKind? agentKindOverride = null,
        string? initialPrompt = null)
    {
        var agentKind = agentKindOverride ?? Profile.AgentKind;
        var argumentsText = argumentsOverride ?? _settings.GetArguments(agentKind);
        var executable = _settings.GetExecutable(agentKind);
        _process = _launcher.Start(Profile, agentKind, executable, argumentsText, resume, _settings.ResumeMode, initialPrompt);
        _process.Exited += OnProcessExited;
        ProcessId = _process.Id;
        IsRunning = true;
        _activeAgentKind = agentKind;
        AgentDisplayName = AgentCatalog.Get(agentKind).DisplayName;
        Profile.LastLaunchedAt = DateTimeOffset.Now;
        Profile.LastUsedAgentKind = agentKind;
        _limitWatcher = new TranscriptLimitWatcher(AgentTranscriptSourceRegistry.Get(agentKind));
        _limitWatcher.Reset(DateTimeOffset.Now);
        ClearAutoResume();
        RefreshLimitStatusSummary();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
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
        ClearAutoResume();
        RefreshLimitStatusSummary();
        ProfileChanged?.Invoke(this, EventArgs.Empty);
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

            // Some CLI versions exit the process immediately after reporting a limit. Poll once
            // while this run is still marked active so the final transcript line is not missed
            // between the normal 20-second checks.
            TryDetectUsageLimit();
            ProfileChanged?.Invoke(this, EventArgs.Empty);

            _process = null;
            IsRunning = false;
            ProcessId = null;
            AgentDisplayName = AgentCatalog.Get(Profile.AgentKind).DisplayName;
            proc.Dispose();
        });
    }
}
