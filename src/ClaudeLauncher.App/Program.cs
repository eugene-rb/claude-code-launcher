using System.IO;
using System.Text;
using System.Windows;
using ClaudeLauncher.App.Services;
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
        // Claude Code invokes `ClaudeLauncher.App.exe usage-statusline` as its status-line command,
        // piping the session JSON on stdin. Handle it before anything WPF/Velopack touches - it's a
        // headless, sub-second render helper, not an app launch.
        if (args is ["usage-statusline", ..])
        {
            // WinExe stdio defaults to the OEM code page, which mangles the "·" separator and the
            // Japanese label. Claude Code reads the status line as UTF-8, so force it.
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var stdin = new StreamReader(Console.OpenStandardInput(), utf8);
            using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8) { AutoFlush = true };
            UsageStatusLineBridge.Run(stdin, stdout);
            return;
        }

        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
