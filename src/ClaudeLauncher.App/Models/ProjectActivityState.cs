namespace ClaudeLauncher.App.Models;

/// <summary>At-a-glance activity state for a project's dashboard badge. <see cref="Unknown"/> means no
/// signal is available yet (e.g. an imported project whose transcript hasn't been touched recently) and
/// the badge should be hidden rather than guess.</summary>
public enum ProjectActivityState
{
    Unknown,
    Idle,
    Responding,
    AwaitingApproval,
}
