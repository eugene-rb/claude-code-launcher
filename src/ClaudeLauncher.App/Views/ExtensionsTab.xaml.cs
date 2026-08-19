using System.Windows.Controls;

namespace ClaudeLauncher.App.Views;

public partial class ExtensionsTab : UserControl
{
    public ExtensionsTab()
    {
        InitializeComponent();
    }

    private void LogTextBox_TextChanged(object sender, TextChangedEventArgs e) => LogTextBox.ScrollToEnd();
}
