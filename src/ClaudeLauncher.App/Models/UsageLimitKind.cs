namespace ClaudeLauncher.App.Models;

/// <summary>Which of Claude's two account-wide rate limits a "You've hit your ... limit" transcript
/// event refers to. See <see cref="Services.UsageLimitEventParser"/>.</summary>
public enum UsageLimitKind
{
    Session,
    Weekly,
}
