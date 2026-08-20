using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;
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

    private static readonly string[] TimeFormats = ["h\\:mm", "hh\\:mm"];

    private readonly SessionProfile _profile;
    private readonly List<string> _otherWorkingDirectories;
    private readonly List<Border> _swatches = [];
    private string _selectedAccentHex;

    public SessionEditWindow(SessionProfile profile, bool isNew, IEnumerable<string> otherWorkingDirectories)
    {
        InitializeComponent();
        SystemThemeWatcher.Watch(this);

        _profile = profile;
        _otherWorkingDirectories = [.. otherWorkingDirectories];
        _selectedAccentHex = string.IsNullOrWhiteSpace(profile.AccentColorHex) ? AccentPalette[0] : profile.AccentColorHex;

        Title = isNew ? "新規プロジェクト" : "プロジェクトを編集";
        NameTextBox.Text = profile.Name;
        WorkingDirectoryTextBox.Text = profile.WorkingDirectory;

        BuildAccentSwatches();
        InitializeScheduleControls(profile);
    }

    private void InitializeScheduleControls(SessionProfile profile)
    {
        // A "Once" schedule whose grace window has already elapsed will never fire again
        // (see ScheduleEvaluator.ShouldFire). Open the dialog with it unchecked so editing an
        // unrelated field isn't blocked by "予約起動の日時は現在より後にしてください。" for a
        // schedule that's effectively dead; the previous date/time still prefill the fields if
        // the user wants to re-enable and pick a new time.
        var isExpiredOnce = profile.Repeat == ScheduleRepeat.Once
            && profile.ScheduledAt is { } scheduledAt
            && scheduledAt + ScheduleEvaluator.GraceWindow < DateTimeOffset.Now;

        ScheduleEnabledCheckBox.IsChecked = profile.ScheduleEnabled && !isExpiredOnce;
        ScheduleDailyRadio.IsChecked = profile.Repeat == ScheduleRepeat.Daily;
        ScheduleOnceRadio.IsChecked = profile.Repeat != ScheduleRepeat.Daily;

        if (profile.ScheduledAt is { } at)
        {
            ScheduleDatePicker.SelectedDate = at.LocalDateTime.Date;
            ScheduleOnceTimeTextBox.Text = at.LocalDateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
        }
        else
        {
            ScheduleDatePicker.SelectedDate = DateTime.Today;
        }

        if (profile.DailyTime is { } dailyTime)
        {
            ScheduleDailyTimeTextBox.Text = dailyTime.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        UpdateScheduleVisibility();
    }

    private void ScheduleOption_Changed(object sender, RoutedEventArgs e) => UpdateScheduleVisibility();

    private void UpdateScheduleVisibility()
    {
        // XAML sets ScheduleOnceRadio's IsChecked="True" declaratively, which fires its Checked
        // event synchronously while InitializeComponent() is still connecting later-declared
        // named elements (ScheduleOncePanel/ScheduleDailyPanel/ScheduleDailyRadio come after the
        // radio buttons in the tree) - their fields are still null at that point. Bail out; the
        // explicit UpdateScheduleVisibility() call at the end of InitializeScheduleControls runs
        // after InitializeComponent() completes and does the real, correct update.
        if (SchedulePanel is null || ScheduleOncePanel is null || ScheduleDailyPanel is null
            || ScheduleOnceRadio is null || ScheduleDailyRadio is null || ScheduleEnabledCheckBox is null)
        {
            return;
        }

        var enabled = ScheduleEnabledCheckBox.IsChecked == true;
        SchedulePanel.IsEnabled = enabled;
        ScheduleOncePanel.Visibility = enabled && ScheduleOnceRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ScheduleDailyPanel.Visibility = enabled && ScheduleDailyRadio.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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

        if (_otherWorkingDirectories.Any(other => WorkingDirectoryComparer.AreSame(other, workingDirectory)))
        {
            ShowValidationError("このディレクトリは既に別のプロジェクトとして登録されています。");
            return;
        }

        var scheduleEnabled = ScheduleEnabledCheckBox.IsChecked == true;
        var repeat = ScheduleDailyRadio.IsChecked == true ? ScheduleRepeat.Daily : ScheduleRepeat.Once;
        DateTimeOffset? scheduledAt = null;
        TimeSpan? dailyTime = null;

        if (scheduleEnabled)
        {
            if (repeat == ScheduleRepeat.Once)
            {
                if (ScheduleDatePicker.SelectedDate is not { } date)
                {
                    ShowValidationError("予約起動の日付を指定してください。");
                    return;
                }

                if (!TimeSpan.TryParseExact(ScheduleOnceTimeTextBox.Text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out var time))
                {
                    ShowValidationError("予約起動の時刻は H:mm 形式(例: 9:00)で入力してください。");
                    return;
                }

                scheduledAt = new DateTimeOffset(date.Date + time);
                if (scheduledAt <= DateTimeOffset.Now)
                {
                    ShowValidationError("予約起動の日時は現在より後にしてください。");
                    return;
                }
            }
            else
            {
                if (!TimeSpan.TryParseExact(ScheduleDailyTimeTextBox.Text.Trim(), TimeFormats, CultureInfo.InvariantCulture, out var time))
                {
                    ShowValidationError("予約起動の時刻は H:mm 形式(例: 9:00)で入力してください。");
                    return;
                }

                dailyTime = time;
            }
        }

        _profile.Name = name;
        _profile.WorkingDirectory = workingDirectory;
        _profile.AccentColorHex = _selectedAccentHex;
        _profile.ScheduleEnabled = scheduleEnabled;
        _profile.Repeat = repeat;
        _profile.ScheduledAt = scheduledAt;
        _profile.DailyTime = dailyTime;

        DialogResult = true;
    }

    private void ShowValidationError(string message)
    {
        ValidationTextBlock.Text = message;
        ValidationTextBlock.Visibility = Visibility.Visible;
    }
}
