using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClaudeLauncher.App.ViewModels;

namespace ClaudeLauncher.App.Views;

/// <summary>
/// "設定ファイル" tab: lists CLAUDE.md / settings.json across user and project scope and lets the
/// user view/edit them in place. DataContext is set by the host (MainWindow) to a ConfigFilesViewModel.
/// </summary>
public partial class ConfigFilesTab : UserControl
{
    private ConfigFilesViewModel ViewModel => (ConfigFilesViewModel)DataContext;

    public ConfigFilesTab()
    {
        InitializeComponent();
    }

    private void EntryRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ConfigFileEntryViewModel entry)
        {
            return;
        }

        if (!ConfirmDiscardUnsavedChanges())
        {
            return;
        }

        ViewModel.SelectEntry(entry);
    }

    private void SessionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SessionComboBox.SelectedItem is SessionItemViewModel session)
        {
            ViewModel.ProjectDirectory = session.WorkingDirectory;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "プロジェクトディレクトリを選択" };
        if (!string.IsNullOrWhiteSpace(ViewModel.ProjectDirectory) && Directory.Exists(ViewModel.ProjectDirectory))
        {
            dialog.InitialDirectory = ViewModel.ProjectDirectory;
        }

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            ViewModel.ProjectDirectory = dialog.FolderName;
        }
    }

    private bool ConfirmDiscardUnsavedChanges()
    {
        if (!ViewModel.IsDirty)
        {
            return true;
        }

        var result = System.Windows.MessageBox.Show(
            Window.GetWindow(this),
            "保存されていない変更があります。破棄して切り替えますか?",
            "未保存の変更",
            System.Windows.MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);

        return result == System.Windows.MessageBoxResult.Yes;
    }
}
