namespace WinStart.Core.Tweaks;

internal static class Keys
{
    public const string ExplorerAdvanced =
        @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public const string ExplorerMachine =
        @"HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer";

    public const string ClassesClsid = @"HKCU\Software\Classes\CLSID";
    public const string ClassesWowClsid = @"HKCU\Software\Classes\Wow6432Node\CLSID";

    public const string Personalization =
        @"HKLM\Software\Policies\Microsoft\Windows\Personalization";

    public const string PoliciesExplorer =
        @"HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    public const string International = @"HKCU\Control Panel\International";
    public const string Desktop = @"HKCU\Control Panel\Desktop";

    public const string Search = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";
    public const string StartMenu = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Start";

    public const string HomeClsid = "{f874310e-b6b7-47dc-bc84-b9e6b38f5903}";
    public const string GalleryClsid = "{e88865ea-0e1c-4e20-9aa6-edcd0212c87c}";
    public const string NetworkClsid = "{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}";

    public const string PinnedValue = "System.IsPinnedToNameSpaceTree";

    public static string Clsid(string guid) => $@"{ClassesClsid}\{guid}";
    public static string WowClsid(string guid) => $@"{ClassesWowClsid}\{guid}";
}
