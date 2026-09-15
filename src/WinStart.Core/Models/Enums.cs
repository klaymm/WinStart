namespace WinStart.Core.Models;

public enum TweakCategory
{
    Interface,
    ContextMenu,
    Cleaning,
    Optimization,
    Apps,
    Components,
    Misc
}

public enum TweakKind
{
    Toggle,
    Action
}

public enum TweakState
{
    Unknown,
    Applied,
    NotApplied,
    Unavailable
}

public enum TweakDirection
{
    Apply,
    Revert
}

public enum TweakStatus
{
    Success,
    Failed,
    Skipped
}
