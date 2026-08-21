using System.Windows;
using ClaudeLauncher.App.Services;
using Wpf.Ui.Appearance;

namespace ClaudeLauncher.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceMutexName = "Local\\ClaudeCodeLauncher-SingleInstance";
    private const string ShowRequestEventName = "Local\\ClaudeCodeLauncher-ShowRequest";
    private const string TrayStartArg = "--tray";

    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private EventWaitHandle? _showRequestEvent;
    private MainWindow? _mainWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        _ownsSingleInstanceMutex = createdNew;
        _showRequestEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowRequestEventName);

        if (!createdNew)
        {
            // Another instance already owns the mutex - ask it to restore its window and exit.
            // Startup-launched (--tray) and manually-launched instances can otherwise both end up
            // running their own tray icon and usage-limit watchers against the same projects.
            _showRequestEvent.Set();
            Shutdown();
            return;
        }

        ApplicationThemeManager.ApplySystemTheme();

        new StartupRegistrationService().RefreshPathIfEnabled();

        var startMinimized = Array.IndexOf(e.Args, TrayStartArg) >= 0;

        _mainWindow = new MainWindow();
        MainWindow = _mainWindow;

        // Always Show() first, even when starting minimized: WPF-UI's NotifyIcon registers itself on
        // the window's Loaded event, which never fires for a window that's never shown - confirmed by
        // launching with --tray and checking TrayIcon.IsRegistered, which came back false. Show() then
        // Hide() gets a real tray icon with no more than a one-frame flash, instead of a resident
        // process with no way to reach it.
        _mainWindow.Show();
        if (startMinimized)
        {
            _mainWindow.Hide();
        }

        ListenForShowRequests();
    }

    private void ListenForShowRequests()
    {
        var showRequestEvent = _showRequestEvent!;
        _ = Task.Run(() =>
        {
            while (true)
            {
                showRequestEvent.WaitOne();
                Dispatcher.Invoke(() => _mainWindow?.RestoreFromTray());
            }
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // A second instance that lost the mutex race never owns it - releasing here would throw
        // ApplicationException ("unsynchronized block of code"). Confirmed by actually launching a
        // second instance against an installed build: it crashed on exit until this guard was added.
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        base.OnExit(e);
    }
}
