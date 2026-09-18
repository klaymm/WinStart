using WinStart.Core.Abstractions;

namespace WinStart.Core.Models;

public sealed class TweakOption
{
    public required string Id { get; init; }
    public required string TitleKey { get; init; }
    public string? Swatch { get; init; }
}

public sealed class TweakDefinition
{
    public required string Id { get; init; }
    public required TweakCategory Category { get; init; }
    public TweakKind Kind { get; init; } = TweakKind.Toggle;

    public bool Destructive { get; init; }

    public int Order { get; init; }

    public string TitleKey => $"tweak.{Id}.title";
    public string DescriptionKey => $"tweak.{Id}.desc";

    public string? ConfirmKey { get; init; }

    public bool Reversible { get; init; } = true;
    public bool NeedsExplorerRestart { get; init; }
    public bool RequiresNetwork { get; init; }

    public int MinBuild { get; init; }

    public IReadOnlyList<TweakOption> Options { get; init; } = [];

    public bool SupportsShiftOption { get; init; }

    public string? ExtendedOptionKey { get; init; }

    public bool ExtendedNeedsExplorerRestart { get; init; }

    public IReadOnlyList<RegistryAction> RegistryActions { get; init; } = [];

    public IReadOnlyList<RegistryAction> RevertActions { get; init; } = [];

    public bool ManualRevertOnly { get; init; }

    public Func<ITweakContext, TweakState>? CustomDetect { get; init; }
    public Func<ITweakContext, CancellationToken, Task>? CustomApply { get; init; }
    public Func<ITweakContext, CancellationToken, Task>? CustomRevert { get; init; }

    public string? ActionVerbKey { get; init; }
}
