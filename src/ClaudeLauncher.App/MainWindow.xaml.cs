using System.Windows;
using System.Windows.Controls;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.ViewModels;
using ClaudeLauncher.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ClaudeLauncher.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : FluentWindow
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        SystemThemeWatcher.Watch(this);
    }

    private void AddSession_Click(object sender, RoutedEventArgs e)
    {
        var profile = new SessionProfile();
        var dialog = new SessionEditWindow(profile, isNew: true) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            ViewModel.AddProfile(profile);
        }
    }

    private void EditSession_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SessionItemViewModel item)
        {
            return;
        }

        var editable = item.Profile.Clone();
        var dialog = new SessionEditWindow(editable, isNew: false) { Owner = this };
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
            $"セッション「{item.Name}」を削除しますか?この操作は取り消せません。",
            "セッションの削除",
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
