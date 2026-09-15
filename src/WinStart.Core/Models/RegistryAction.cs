using Microsoft.Win32;

namespace WinStart.Core.Models;

public enum RegistryOp
{
    SetValue,
    DeleteValue,
    DeleteKey
}

public sealed class RegistryAction
{
    public required RegistryOp Op { get; init; }

    public required string Path { get; init; }

    public string? Name { get; init; }

    public RegistryValueKind ValueKind { get; init; } = RegistryValueKind.DWord;

    public object? Value { get; init; }

    public bool SkipInDetect { get; init; }

    public static RegistryAction Set(string path, string? name, int value) => new()
    { Op = RegistryOp.SetValue, Path = path, Name = name, ValueKind = RegistryValueKind.DWord, Value = value };

    public static RegistryAction SetString(string path, string? name, string value) => new()
    { Op = RegistryOp.SetValue, Path = path, Name = name, ValueKind = RegistryValueKind.String, Value = value };

    public static RegistryAction SetBinary(string path, string? name, byte[] value) => new()
    { Op = RegistryOp.SetValue, Path = path, Name = name, ValueKind = RegistryValueKind.Binary, Value = value };

    public static RegistryAction DeleteValue(string path, string name) => new()
    { Op = RegistryOp.DeleteValue, Path = path, Name = name };

    public static RegistryAction DeleteKey(string path) => new()
    { Op = RegistryOp.DeleteKey, Path = path };
}
