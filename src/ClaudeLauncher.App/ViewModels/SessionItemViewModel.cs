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

    public SessionItemViewModel(SessionProfile profile, ProcessLauncherService launcher)
    {
        Profile = profile;
        _launcher = launcher;
        name = profile.Name;
        workingDirectory = profile.WorkingDirectory;
        executable = profile.Executable;
        arguments = profile.Arguments;
        accentColorHex = profile.AccentColorHex;
    }

    public void ApplyProfile(SessionProfile updated)
    {
        Profile = updated;
        Name = updated.Name;
        WorkingDirectory = updated.WorkingDirectory;
        Executable = updated.Executable;
        Arguments = updated.Arguments;
        AccentColorHex = updated.AccentColorHex;
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
