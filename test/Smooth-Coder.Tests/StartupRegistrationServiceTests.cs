using SmoothCoder.App.Services;
using Microsoft.Win32;

namespace SmoothCoder.Tests;

public class StartupRegistrationServiceTests
{
    private static RegistryKey CreateTempBaseKey(out string subKeyName)
    {
        subKeyName = "SmoothCoderTests_" + Guid.NewGuid().ToString("N");
        return Registry.CurrentUser.CreateSubKey(subKeyName, writable: true);
    }

    [Fact]
    public void IsEnabled_NoValue_ReturnsFalse()
    {
        using var baseKey = CreateTempBaseKey(out var subKeyName);
        try
        {
            var service = new StartupRegistrationService(baseKey);

            Assert.False(service.IsEnabled());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void SetEnabled_True_WritesExecutablePathWithTrayArg()
    {
        using var baseKey = CreateTempBaseKey(out var subKeyName);
        try
        {
            var service = new StartupRegistrationService(baseKey);

            service.SetEnabled(true);

            Assert.True(service.IsEnabled());

            using var runKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            var command = Assert.IsType<string>(runKey!.GetValue("Smooth-Coder"));
            Assert.Contains("--tray", command);
            Assert.Contains(Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0], command);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void SetEnabled_FalseAfterTrue_RemovesValue()
    {
        using var baseKey = CreateTempBaseKey(out var subKeyName);
        try
        {
            var service = new StartupRegistrationService(baseKey);
            service.SetEnabled(true);

            service.SetEnabled(false);

            Assert.False(service.IsEnabled());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void RefreshPathIfEnabled_WhenDisabled_DoesNotCreateRunKey()
    {
        using var baseKey = CreateTempBaseKey(out var subKeyName);
        try
        {
            var service = new StartupRegistrationService(baseKey);

            service.RefreshPathIfEnabled();

            using var runKey = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            Assert.Null(runKey);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void RefreshPathIfEnabled_WhenEnabled_RewritesCurrentPath()
    {
        using var baseKey = CreateTempBaseKey(out var subKeyName);
        try
        {
            var service = new StartupRegistrationService(baseKey);
            service.SetEnabled(true);

            service.RefreshPathIfEnabled();

            Assert.True(service.IsEnabled());
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);
        }
    }
}
