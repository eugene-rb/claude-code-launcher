namespace ClaudeLauncher.App.Models;

public sealed record DiscoveredProjectInfo(string WorkingDirectory, string SuggestedName, DateTimeOffset LastActivityAt);
