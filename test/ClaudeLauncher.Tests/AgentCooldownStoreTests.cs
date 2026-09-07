using System.IO;
using ClaudeLauncher.App.Models;
using ClaudeLauncher.App.Services;

namespace ClaudeLauncher.Tests;

public class AgentCooldownStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ClaudeLauncherTests", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_directory, "agent-cooldowns.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Get_UnknownAgent_ReturnsNull()
    {
        var store = new AgentCooldownStore(FilePath);

        Assert.Null(store.Get(AgentKind.ClaudeCode, Now));
        Assert.False(store.IsCoolingDown(AgentKind.ClaudeCode, Now));
    }

    [Fact]
    public void Set_ThenGet_RoundTripsThroughDisk()
    {
        var resetAt = Now.AddHours(3);

        Assert.True(new AgentCooldownStore(FilePath).Set(AgentKind.CodexCli, resetAt, Now));

        // A separate instance, so this reads the file rather than the first instance's memory - the
        // whole reason the ledger is persisted is that an app restart must not forget it.
        var reloaded = new AgentCooldownStore(FilePath);
        Assert.Equal(resetAt, reloaded.Get(AgentKind.CodexCli, Now));
        Assert.Null(reloaded.Get(AgentKind.ClaudeCode, Now));
    }

    [Fact]
    public void Get_PastResetTime_ReadsAsNotLimited()
    {
        var store = new AgentCooldownStore(FilePath);
        store.Set(AgentKind.ClaudeCode, Now.AddHours(1), Now);

        Assert.Null(store.Get(AgentKind.ClaudeCode, Now.AddHours(2)));
    }

    [Fact]
    public void Set_ResetAlreadyInThePast_IsIgnored()
    {
        var store = new AgentCooldownStore(FilePath);

        Assert.False(store.Set(AgentKind.ClaudeCode, Now.AddMinutes(-1), Now));
        Assert.Null(store.Get(AgentKind.ClaudeCode, Now));
    }

    [Fact]
    public void Set_LaterResetWins_EarlierIsIgnored()
    {
        // Two limits can be in force at once (5-hour and weekly); the account is only usable again
        // once the longer of them has passed.
        var store = new AgentCooldownStore(FilePath);
        var weekly = Now.AddDays(3);

        store.Set(AgentKind.ClaudeCode, weekly, Now);
        Assert.False(store.Set(AgentKind.ClaudeCode, Now.AddHours(2), Now));

        Assert.Equal(weekly, store.Get(AgentKind.ClaudeCode, Now));
    }

    [Fact]
    public void PruneExpired_RemovesOnlyElapsedEntries()
    {
        var store = new AgentCooldownStore(FilePath);
        store.Set(AgentKind.ClaudeCode, Now.AddHours(1), Now);
        store.Set(AgentKind.CodexCli, Now.AddHours(6), Now);

        Assert.True(store.PruneExpired(Now.AddHours(2)));
        Assert.False(store.PruneExpired(Now.AddHours(2)));

        var reloaded = new AgentCooldownStore(FilePath);
        Assert.Null(reloaded.Get(AgentKind.ClaudeCode, Now.AddHours(2)));
        Assert.NotNull(reloaded.Get(AgentKind.CodexCli, Now.AddHours(2)));
    }

    [Fact]
    public void Load_CorruptFile_ReadsAsEmptyWithoutThrowing()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ this is not json");

        var store = new AgentCooldownStore(FilePath);

        Assert.Null(store.Get(AgentKind.ClaudeCode, Now));
        // Still usable afterwards: a broken ledger must degrade to "nothing is limited", not break
        // every subsequent failover decision.
        Assert.True(store.Set(AgentKind.ClaudeCode, Now.AddHours(1), Now));
    }
}
