using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeLauncher.App.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ClaudeLauncher.App.Views;

/// <summary>
/// Add/edit dialog for a session profile. The caller passes the exact SessionProfile instance to
/// mutate (a clone when editing, so a Cancel leaves the original untouched) and reads back the
/// result via <see cref="Window.DialogResult"/>.
/// </summary>
public partial class SessionEditWindow : FluentWindow
{
    private static readonly string[] AccentPalette =
    [
        "#0078D4", "#8764B8", "#00CC6A", "#FFB900", "#E74856", "#00B7C3", "#FF8C00", "#767676",
    ];

    private readonly SessionProfile _profile;
    private readonly List<Border> _swatches = [];
    private string _selectedAccentHex;

    public SessionEditWindow(SessionProfile profile, bool isNew)
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);

        _profile = profile;
        _selectedAccentHex = string.IsNullOrWhiteSpace(profile.AccentColorHex) ? AccentPalette[0] : profile.AccentColorHex;

        Title = isNew ? "新規セッション" : "セッションを編集";
        NameTextBox.Text = profile.Name;
        WorkingDirectoryTextBox.Text = profile.WorkingDirectory;
        ExecutableTextBox.Text = string.IsNullOrWhiteSpace(profile.Executable) ? "claude" : profile.Executable;
        ArgumentsTextBox.Text = profile.Arguments;

        BuildAccentSwatches();
    }

    private void BuildAccentSwatches()
    {
        foreach (var hex in AccentPalette)
        {
            var border = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            border.MouseLeftButtonUp += Swatch_Click;
            _swatches.Add(border);
            AccentColorPanel.Children.Add(border);
        }

        RefreshSwatchSelection();
    }

    private void Swatch_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { Tag: string hex })
        {
            _selectedAccentHex = hex;
            RefreshSwatchSelection();
        }
    }

    private void RefreshSwatchSelection()
    {
        foreach (var swatch in _swatches)
        {
            var isSelected = Equals(swatch.Tag as string, _selectedAccentHex);
            swatch.BorderBrush = isSelected ? SystemColors.HighlightBrush : Brushes.Transparent;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "作業ディレクトリを選択" };
        if (!string.IsNullOrWhiteSpace(WorkingDirectoryTextBox.Text) && Directory.Exists(WorkingDirectoryTextBox.Text))
        {
            dialog.InitialDirectory = WorkingDirectoryTextBox.Text;
        }

        if (dialog.ShowDialog(this) == true)
        {
            WorkingDirectoryTextBox.Text = dialog.FolderName;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var workingDirectory = WorkingDirectoryTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(name))
        {
            ShowValidationError("名前を入力してください。");
            return;
        }

        if (string.IsNullOrWhiteSpace(workingDirectory) || !Directory.Exists(workingDirectory))
        {
            ShowValidationError("作業ディレクトリが存在しません。正しいパスを指定してください。");
            return;
        }

        _profile.Name = name;
        _profile.WorkingDirectory = workingDirectory;
        _profile.Executable = string.IsNullOrWhiteSpace(ExecutableTextBox.Text) ? "claude" : ExecutableTextBox.Text.Trim();
        _profile.Arguments = ArgumentsTextBox.Text.Trim();
        _profile.AccentColorHex = _selectedAccentHex;

        DialogResult = true;
    }

    private void ShowValidationError(string message)
    {
        ValidationTextBlock.Text = message;
        ValidationTextBlock.Visibility = Visibility.Visible;
    }
}
