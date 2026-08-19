using System.Diagnostics;
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
    private void Start()
    {
        _process = _launcher.Start(Profile);
        _process.Exited += OnProcessExited;
        ProcessId = _process.Id;
        IsRunning = true;
        Profile.LastLaunchedAt = DateTimeOffset.Now;
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
