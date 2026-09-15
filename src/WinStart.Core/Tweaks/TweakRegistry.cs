using WinStart.Core.Models;

namespace WinStart.Core.Tweaks;

public sealed class TweakRegistry
{
    public IReadOnlyList<TweakDefinition> All { get; }

    public TweakRegistry()
    {
        All =
        [
            .. InterfaceTweaks.All(),
            .. ContextMenuTweaks.All(),
            .. CleaningTweaks.All(),
            .. OptimizationTweaks.All(),
            .. AppsTweaks.All(),
            .. ComponentsTweaks.All(),
            .. MiscTweaks.All()
        ];

        var duplicates = All.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0)
            throw new InvalidOperationException("Дублирующиеся Id твиков: " + string.Join(", ", duplicates));
    }

    public IEnumerable<TweakDefinition> ByCategory(TweakCategory category) =>
        All.Where(t => t.Category == category).OrderBy(t => t.Order);

    public TweakDefinition? ById(string id) => All.FirstOrDefault(t => t.Id == id);
}
