namespace ClaudeLauncher.App.Models;

public sealed record ConfigFileDefinition(
    string Key,
    string DisplayName,
    string Description,
    string RelativePath,
    ConfigFileScope Scope);
