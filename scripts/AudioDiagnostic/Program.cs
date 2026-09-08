using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using System.Windows;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        var directory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeCodeLauncher", "current");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            var path = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
        };
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(directory, "ClaudeLauncher.App.dll"));
        Console.WriteLine($"Installed assembly: {assembly.FullName}");
        var application = new Application();
        var type = assembly.GetType("ClaudeLauncher.App.Services.VoiceNotificationService", true)!;
        var service = Activator.CreateInstance(type, new object[] { null })!;
        type.GetProperty("Volume")!.SetValue(service, 1.0);
        type.GetMethod("PlayTest")!.Invoke(service, new object[] { Type.Missing });
        Thread.Sleep(4000);
        Console.WriteLine("LastError: " + (type.GetProperty("LastError")!.GetValue(service) ?? "none"));
    }
}
