using Microsoft.Win32;

namespace WinStart.Core.Models;

public sealed class RegistryValueSnapshot
{
    public string Path { get; set; } = "";
    public string? Name { get; set; }
    public bool KeyExisted { get; set; }
    public bool ValueExisted { get; set; }
    public RegistryValueKind Kind { get; set; } = RegistryValueKind.Unknown;

    public string? Data { get; set; }
}

public sealed class RegistryKeyBackup
{
    public string Path { get; set; } = "";
    public bool Existed { get; set; }
    public string? File { get; set; }
}
