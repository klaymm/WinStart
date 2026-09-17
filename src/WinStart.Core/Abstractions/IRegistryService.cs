using Microsoft.Win32;
using WinStart.Core.Models;

namespace WinStart.Core.Abstractions;

public interface IRegistryService
{
    bool KeyExists(string path);
    bool ValueExists(string path, string? name);

    IReadOnlyList<string> SubKeys(string path);

    object? GetValue(string path, string? name);
    string? GetString(string path, string? name);
    int? GetInt(string path, string? name);

    void SetValue(string path, string? name, object value, RegistryValueKind kind);
    void DeleteValue(string path, string? name);
    void DeleteKey(string path);

    RegistryValueSnapshot Snapshot(string path, string? name);

    void Restore(RegistryValueSnapshot snapshot);

    Task<bool> ExportKeyAsync(string path, string file, CancellationToken ct);

    Task<bool> ImportKeyAsync(string file, CancellationToken ct);
}
