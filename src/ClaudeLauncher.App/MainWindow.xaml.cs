using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.ViewModels;
using ClaudeLauncher.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Tray.Controls;

namespace ClaudeLauncher.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    private const string DefaultTrayTooltip = "Claude Code ランチャー";

    private bool _isExiting;

    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        SystemThemeWatcher.Watch(this);

        ViewModel.Update.BeforeApply = () => TrayIcon.Dispose();
        ViewModel.Update.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(UpdateViewModel.State) && ViewModel.Update.State == UpdateState.ReadyToApply && !IsVisible)
            {
                TrayIcon.TooltipText = "アップデートの準備ができました - クリックして開く";
            }
        };
    }

    /// <summary>Shown by the tray icon's "開く" item, its left-click, and when a second app instance
    /// signals the running one to come forward (see <see cref="App.OnStartup"/>).</summary>
    public void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        TrayIcon.TooltipText = DefaultTrayTooltip;
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        // Closing the window minimizes to the tray instead of exiting - real exit only happens via
        // the tray icon's "終了" item (TrayExit_Click), which sets _isExiting first.
        e.Cancel = true;
        Hide();
    }

    private void TrayIcon_LeftClick(NotifyIcon sender, RoutedEventArgs e) => RestoreFromTray();

    private void TrayOpen_Click(object sender, RoutedEventArgs e) => RestoreFromTray();

    private void TrayExit_Click(object sender, RoutedEventArgs e)
    {
        _isExiting = true;
        TrayIcon.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void AddSession_Click(object sender, RoutedEventArgs e)
    {
        var profile = new SessionProfile();
        var otherDirectories = ViewModel.Sessions.Select(s => s.WorkingDirectory);
        var dialog = new SessionEditWindow(profile, isNew: true, otherDirectories) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.AddProfile(profile);
        }
    }

    private void ImportProjects_Click(object sender, RoutedEventArgs e)
    {
        var registered = ViewModel.Sessions.Select(s => s.WorkingDirectory);
        var dialog = new ImportProjectWindow(registered) { Owner = this };
        if (dialog.ShowDialog() == true && dialog.SelectedProfiles.Count > 0)
        {
            ViewModel.AddProfiles(dialog.SelectedProfiles);
        }
    }

    private void EditSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionItemViewModel item)
        {
            return;
        }

        var editable = item.Profile.Clone();
        var otherDirectories = ViewModel.Sessions.Where(s => s != item).Select(s => s.WorkingDirectory);
        var dialog = new SessionEditWindow(editable, isNew: false, otherDirectories) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.ApplyEdit(item, editable);
        }
    }

    private void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionItemViewModel item)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"プロジェクト「{item.Name}」を削除しますか?この操作は取り消せません。",
            "プロジェクトの削除",
            System.Windows.MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);

        if (result == System.Windows.MessageBoxResult.Yes)
        {
            ViewModel.RemoveSession(item);
        }
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Selector.SelectionChanged bubbles - a ComboBox/ListBox selection changing inside a tab's
        // content would also reach this handler. Only react when the TabControl itself is the
        // element whose selection changed.
        if (e.OriginalSource is not TabControl || !ExtensionsTabItem.IsSelected)
        {
            return;
        }

        ViewModel.Extensions.EnsureLoaded();
    }
}
