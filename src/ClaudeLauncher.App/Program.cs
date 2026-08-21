using System.Windows;
using Velopack;

namespace ClaudeLauncher.App;

/// <summary>Custom entry point (replaces the App.xaml-generated Main) so <see cref="VelopackApp.Run"/>
/// can run first, before any WPF window is created — required by Velopack to handle install/update/
/// uninstall hooks correctly.</summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
