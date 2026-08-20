using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;
using Wpf.Ui.Controls;

namespace ClaudeLauncher.App.Views;

/// <summary>Lets the user pick from real Claude Code projects (~/.claude/projects) that aren't yet
/// registered as sessions in this app, and turns the selected ones into new <see cref="SessionProfile"/>s.</summary>
public partial class ImportProjectWindow : FluentWindow
{
    private static readonly string[] AccentPalette =
    [
        "#0078D4", "#8764B8", "#00CC6A", "#FFB900", "#E74856", "#00B7C3", "#FF8C00", "#767676",
    ];

    private readonly ObservableCollection<ImportRow> _rows = [];
    private readonly ICollectionView _rowsView;

    public IReadOnlyList<SessionProfile> SelectedProfiles { get; private set; } = [];

    public ImportProjectWindow(IEnumerable<string> alreadyRegisteredWorkingDirectories)
    {
        InitializeComponent();

        var registered = alreadyRegisteredWorkingDirectories.ToList();
        var discovered = ExistingProjectScanner.Scan(ClaudeProjectPathResolver.GetProjectsRoot())
            .Where(p => !registered.Any(r => WorkingDirectoryComparer.AreSame(r, p.WorkingDirectory)))
            .OrderByDescending(p => p.LastActivityAt);

        foreach (var info in discovered)
        {
            _rows.Add(new ImportRow(info));
        }

        RowsItemsControl.ItemsSource = _rows;
        _rowsView = CollectionViewSource.GetDefaultView(_rows);
        _rowsView.Filter = FilterRow;

        EmptyStateText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool FilterRow(object obj)
    {
        if (obj is not ImportRow row)
        {
            return false;
        }

        var query = SearchTextBox.Text.Trim();
        if (query.Length == 0)
        {
            return true;
        }

        return row.Info.SuggestedName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || row.Info.WorkingDirectory.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void SearchTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => _rowsView.Refresh();

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var selected = _rows.Where(r => r.IsSelected).ToList();
        SelectedProfiles = selected.Select((row, index) => new SessionProfile
        {
            Name = row.Info.SuggestedName,
            WorkingDirectory = row.Info.WorkingDirectory,
            Executable = "claude",
            Arguments = string.Empty,
            AccentColorHex = AccentPalette[index % AccentPalette.Length],
        }).ToList();

        DialogResult = true;
    }

    private sealed class ImportRow(DiscoveredProjectInfo info)
    {
        public DiscoveredProjectInfo Info { get; } = info;

        public bool IsSelected { get; set; }

        public string LastActivityText => $"最終利用: {Info.LastActivityAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture)}";
    }
}
