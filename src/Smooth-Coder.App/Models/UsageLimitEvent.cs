namespace SmoothCoder.App.Models;

/// <summary>A parsed "You've hit your session/weekly limit · resets ..." transcript event, with which
/// limit it refers to alongside the reset time. See <see cref="Services.UsageLimitEventParser"/>.</summary>
public readonly record struct UsageLimitEvent(UsageLimitKind Kind, DateTimeOffset ResetAt);
