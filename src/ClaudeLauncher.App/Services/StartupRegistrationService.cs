using Microsoft.Win32;

namespace ClaudeLauncher.App.Services;

/// <summary>Registers/unregisters this app to launch at Windows sign-in via the per-user
/// `HKCU\...\Run` key. This is the single source of truth for "start with Windows" - nothing is
/// mirrored into <see cref="Models.AppSettings"/>, so there's no way for the two to drift apart.</summary>
public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClaudeCodeLauncher";
    private const string TrayStartArg = "--tray";

    private readonly RegistryKey _baseKey;

    public StartupRegistrationService()
        : this(Registry.CurrentUser)
    {
    }

    /// <summary>Test seam - pass a disposable temp key (e.g. a subkey under HKCU created for the test)
    /// so tests never touch the real Run key.</summary>
    public StartupRegistrationService(RegistryKey baseKey)
    {
        _baseKey = baseKey;
    }

    public bool IsEnabled()
    {
        using var key = _baseKey.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public void SetEnabled(bool enabled)
    {
        using var key = _baseKey.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand());
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    /// <summary>Rewrites the registered command to the current executable path. Velopack swaps the
    /// `current\` folder in place on every update, so a path captured once could point at a location
    /// that no longer exists by the next sign-in; calling this on every normal startup keeps it correct.
    /// No-op when startup isn't enabled.</summary>
    public void RefreshPathIfEnabled()
    {
        if (IsEnabled())
        {
            SetEnabled(true);
        }
    }

    private static string BuildCommand()
    {
        var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
        return $"\"{exePath}\" {TrayStartArg}";
    }
}
