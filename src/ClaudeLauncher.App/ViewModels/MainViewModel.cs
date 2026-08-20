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

    private readonly SessionProfileStore _store;
    private readonly ProcessLauncherService _launcher;
    private readonly DispatcherTimer _scheduleTimer;

    public ObservableCollection<SessionItemViewModel> Sessions { get; } = [];

    public ConfigFilesViewModel ConfigFiles { get; }

    public ExtensionsViewModel Extensions { get; } = new();

    [ObservableProperty]
    private int awaitingApprovalCount;

    [ObservableProperty]
    private int respondingCount;

    [ObservableProperty]
    private int idleCount;

    public MainViewModel()
        : this(new SessionProfileStore(), new ProcessLauncherService())
    {
    }

    public MainViewModel(SessionProfileStore store, ProcessLauncherService launcher)
    {
        _store = store;
        _launcher = launcher;

        foreach (var profile in _store.Load())
        {
            Sessions.Add(new SessionItemViewModel(profile, _launcher));
        }

        ConfigFiles = new ConfigFilesViewModel(Sessions);
        RefreshActivitySummary();

        _scheduleTimer = new DispatcherTimer { Interval = ScheduleCheckInterval };
        _scheduleTimer.Tick += (_, _) => CheckSchedules();
        _scheduleTimer.Start();
    }

    private void CheckSchedules()
    {
        var now = DateTimeOffset.Now;
        var anyFired = false;

        var markers = StatusMarkerStore.ReadFresh(StatusMarkerStore.GetDefaultDirectory(), StatusMarkerStore.DefaultMaxAge, now);

        foreach (var session in Sessions)
        {
            // Runs for every project regardless of ScheduleEnabled/IsRunning - imported projects this
            // app never launched still need their dashboard badge kept current.
            session.RefreshActivityState(markers);

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

        RefreshActivitySummary();

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

    public void AddProfile(SessionProfile profile)
    {
        Sessions.Add(new SessionItemViewModel(profile, _launcher));
        RefreshActivitySummary();
        Persist();
    }

    public void AddProfiles(IEnumerable<SessionProfile> profiles)
    {
        foreach (var profile in profiles)
        {
            Sessions.Add(new SessionItemViewModel(profile, _launcher));
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
