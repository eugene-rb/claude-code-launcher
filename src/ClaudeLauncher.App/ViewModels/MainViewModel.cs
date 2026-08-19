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

        _scheduleTimer = new DispatcherTimer { Interval = ScheduleCheckInterval };
        _scheduleTimer.Tick += (_, _) => CheckSchedules();
        _scheduleTimer.Start();
    }

    private void CheckSchedules()
    {
        var now = DateTimeOffset.Now;
        var anyFired = false;

        foreach (var session in Sessions)
        {
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

    public void AddProfile(SessionProfile profile)
    {
        Sessions.Add(new SessionItemViewModel(profile, _launcher));
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
        Persist();
    }

    private void Persist() => _store.Save(Sessions.Select(s => s.Profile));
}
