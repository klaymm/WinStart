using WinStart.Core.Models;

namespace WinStart.Core.Tweaks;

internal static class MiscTweaks
{
    private const string StickyKeys = @"HKCU\Control Panel\Accessibility\StickyKeys";

    public static IEnumerable<TweakDefinition> All()
    {
        yield return DisableStickyKeys();
    }

    private static TweakDefinition DisableStickyKeys() => new()
    {
        Id = "misc.stickyKeys",
        Category = TweakCategory.Misc,
        Order = 1,
        RegistryActions = [RegistryAction.SetString(StickyKeys, "Flags", "506")],
        RevertActions = [RegistryAction.SetString(StickyKeys, "Flags", "510")]
    };
}
