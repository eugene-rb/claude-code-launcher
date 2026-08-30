using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Threading;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClaudeLauncher.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly TimeSpan ScheduleCheckInterval = TimeSpan.FromSeconds(20);

    /// <summary>Cadence for the dashboard's live preview (and activity badge). Much shorter than
    /// <see cref="ScheduleCheckInterval"/> since this only re-reads a bounded tail of each project's
    /// transcript file - cheap enough to poll often - and "real-time" preview would otherwise lag up to
    /// 20 seconds behind whatever Claude just did.</summary>
    private static readonly TimeSpan DashboardRefreshInterval = TimeSpan.FromSeconds(2);

    /// <summary>How recent a status-line snapshot reading has to be for the bars to show it as the
    /// live "実測" value. Older than this and no Claude session has been active to refresh it, so the
    /// bars fall back to the token-based "推定".</summary>
    private static readonly TimeSpan RealReadingFreshness = TimeSpan.FromMinutes(15);

    private readonly SessionProfileStore _store;
    private readonly ProcessLauncherService _launcher;
    private readonly ClaudeAccountUsageTracker _usageTracker;
    private readonly DispatcherTimer _scheduleTimer;
    private readonly DispatcherTimer _dashboardTimer;

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    public ConfigFilesViewModel ConfigFiles { get; }

    public ExtensionsViewModel Extensions { get; } = new();

    public SettingsViewModel Settings { get; }

    public UpdateViewModel Update { get; }

    [ObservableProperty]
    private int awaitingApprovalCount;

    [ObservableProperty]
    private int respondingCount;

    [ObservableProperty]
    private int idleCount;

    /// <summary>% of Claude's account-wide 5-hour/weekly usage limit consumed so far, or null if that
    /// window has never been calibrated yet (its limit hasn't been hit once) - see
    /// <see cref="ClaudeAccountUsageTracker"/>. Account-wide, not per-project, so shown once on the
    /// dashboard rather than per session card.</summary>
    [ObservableProperty]
    private double? sessionWindowUsagePercent;

    [ObservableProperty]
    private double? weeklyWindowUsagePercent;

    /// <summary>True when the corresponding percentage is a real reading from Claude Code's status
    /// line ("実測"); false when it's the token-based estimate ("推定"). Bound by the bar's label.</summary>
    [ObservableProperty]
    private bool sessionWindowUsageIsMeasured;

    [ObservableProperty]
    private bool weeklyWindowUsageIsMeasured;

    public MainViewModel()
        : this(new SessionProfileStore(), new ProcessLauncherService())
    {
    }

    public MainViewModel(SessionProfileStore store, ProcessLauncherService launcher)
    {
        _store = store;
        _launcher = launcher;
        _usageTracker = new ClaudeAccountUsageTracker();
        Update = new UpdateViewModel();
        Settings = new SettingsViewModel(new AppSettingsStore(), new StartupRegistrationService(), Update);

        foreach (var profile in _store.Load())
        {
            Sessions.Add(new SessionItemViewModel(profile, _launcher, Settings));
        }

        ConfigFiles = new ConfigFilesViewModel(Sessions);
        RefreshActivitySummary();

        // One-time synchronous backfill scan (see ClaudeAccountUsageTracker.Poll) so the bars show a
        // real percentage from the moment the window opens, not just after the first schedule tick.
        _usageTracker.Poll(DateTimeOffset.Now);
        RefreshUsagePercentages(DateTimeOffset.Now);

        _scheduleTimer = new DispatcherTimer { Interval = ScheduleCheckInterval };
        _scheduleTimer.Tick += (_, _) => CheckSchedules();
        _scheduleTimer.Start();

        _dashboardTimer = new DispatcherTimer { Interval = DashboardRefreshInterval };
        _dashboardTimer.Tick += (_, _) => RefreshDashboard();
        _dashboardTimer.Start();
    }

    private void RefreshDashboard()
    {
        var markers = StatusMarkerStore.ReadFresh(StatusMarkerStore.GetDefaultDirectory(), StatusMarkerStore.DefaultMaxAge, DateTimeOffset.Now);

        foreach (var session in Sessions)
        {
            // Runs for every project regardless of ScheduleEnabled/IsRunning - imported projects this
            // app never launched still need their dashboard badge and preview kept current.
            session.RefreshActivityState(markers);
        }

        RefreshActivitySummary();
    }

    private void CheckSchedules()
    {
        var now = DateTimeOffset.Now;
        var anyFired = false;

        // Scanning every ~/.claude/projects transcript is heavier than the 2s dashboard tick's bounded
        // tail reads, but incremental (offset-based) after the first call, so it rides this slower timer
        // rather than getting its own.
        _usageTracker.Poll(now);
        RefreshUsagePercentages(now);

        foreach (var session in Sessions)
        {
            if (session.TryDetectUsageLimit())
            {
                anyFired = true;
            }

            if (session.TryFireAutoResume(now))
            {
                anyFired = true;
            }
            else
            {
                session.RefreshLimitStatusSummary();
            }

            if (session.TryFireScheduledLaunch(now))
            {
                anyFired = true;
            }
            else
            {
                // Keeps a "Once" schedule's badge from claiming 起動予定 forever once its grace
                // window has elapsed without firing (e.g. the app was closed through it).
                session.RefreshScheduleSummary();
            }
        }

        if (anyFired)
        {
            Persist();
        }
    }

    private void RefreshActivitySummary()
    {
        AwaitingApprovalCount = Sessions.Count(s => s.ActivityState == ProjectActivityState.AwaitingApproval);
        RespondingCount = Sessions.Count(s => s.ActivityState == ProjectActivityState.Responding);
        IdleCount = Sessions.Count(s => s.ActivityState == ProjectActivityState.Idle);
    }

    private void RefreshUsagePercentages(DateTimeOffset now)
    {
        (SessionWindowUsagePercent, SessionWindowUsageIsMeasured) = EvaluateUsageWindow(
            now, ClaudeAccountUsageTracker.SessionWindow,
            _usageTracker.SessionWindowLastRealPercent, _usageTracker.SessionWindowLastRealAt,
            _usageTracker.SessionWindowResetsAt, _usageTracker.SessionWindowBaselineTokens);

        (WeeklyWindowUsagePercent, WeeklyWindowUsageIsMeasured) = EvaluateUsageWindow(
            now, ClaudeAccountUsageTracker.WeeklyWindow,
            _usageTracker.WeeklyWindowLastRealPercent, _usageTracker.WeeklyWindowLastRealAt,
            _usageTracker.WeeklyWindowResetsAt, _usageTracker.WeeklyWindowBaselineTokens);
    }

    /// <summary>Prefers a fresh real reading from the status-line snapshot; otherwise falls back to the
    /// token estimate - summed against the real block window when its <c>resets_at</c> boundary is
    /// known and still ahead, else a rolling window.</summary>
    private (double? Percent, bool IsMeasured) EvaluateUsageWindow(
        DateTimeOffset now, TimeSpan window,
        double? lastRealPercent, DateTimeOffset? lastRealAt, DateTimeOffset? resetsAt, long? baselineTokens)
    {
        if (lastRealPercent is { } real && lastRealAt is { } capturedAt
            && now - capturedAt < RealReadingFreshness
            && resetsAt is { } reset && reset > now)
        {
            return (real, true);
        }

        var windowTokens = resetsAt is { } r && r > now
            ? _usageTracker.GetWindowTotalSince(r - window)
            : _usageTracker.GetWindowTotal(window, now);

        return (UsageWindowEvaluator.ComputePercentage(windowTokens, baselineTokens), false);
    }

    public void AddProfile(SessionProfile profile)
    {
        Sessions.Add(new SessionItemViewModel(profile, _launcher, Settings));
        RefreshActivitySummary();
        Persist();
    }

    public void AddProfiles(IEnumerable<SessionProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            Sessions.Add(new SessionItemViewModel(profile, _launcher, Settings));
        }

        RefreshActivitySummary();
        Persist();
    }

    public void ApplyEdit(SessionItemViewModel item, SessionProfile updated)
    {
        item.ApplyProfile(updated);
        Persist();
    }

    public void RemoveSession(SessionItemViewModel item)
    {
        Sessions.Remove(item);
        RefreshActivitySummary();
        Persist();
    }

    private void Persist() => _store.Save(Sessions.Select(s => s.Profile));
}
