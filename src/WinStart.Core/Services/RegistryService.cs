using System.Globalization;
using Microsoft.Win32;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;

namespace WinStart.Core.Services;

public sealed class RegistryService : IRegistryService
{
    private readonly IProcessRunner _process;

    public RegistryService(IProcessRunner process) => _process = process;

    private static (RegistryKey Base, string SubKey) Split(string path)
    {
        var raw = path.Replace('/', '\\').TrimStart('\\');
        var idx = raw.IndexOf('\\');
        var root = (idx < 0 ? raw : raw[..idx]).ToUpperInvariant();
        var sub = idx < 0 ? "" : raw[(idx + 1)..];

        var hive = root switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKCR" or "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKU" or "HKEY_USERS" => RegistryHive.Users,
            "HKCC" or "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
            _ => throw new ArgumentException($"Unknown registry root: {root}", nameof(path))
        };

        return (RegistryKey.OpenBaseKey(hive, RegistryView.Registry64), sub);
    }

    private static RegistryKey? Open(string path, bool writable)
    {
        var (baseKey, sub) = Split(path);
        if (sub.Length == 0) return baseKey;

        using (baseKey) return baseKey.OpenSubKey(sub, writable);
    }

    public bool KeyExists(string path)
    {
        try
        {
            using var key = Open(path, false);
            return key is not null;
        }
        catch { return false; }
    }

    public bool ValueExists(string path, string? name)
    {
        try
        {
            using var key = Open(path, false);
            if (key is null) return false;
            return key.GetValue(name ?? "", null) is not null;
        }
        catch { return false; }
    }

    public IReadOnlyList<string> SubKeys(string path)
    {
        try
        {
            using var key = Open(path, false);
            return key is null ? [] : key.GetSubKeyNames();
        }
        catch { return []; }
    }

    public object? GetValue(string path, string? name)
    {
        try
        {
            using var key = Open(path, false);
            return key?.GetValue(name ?? "", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        }
        catch { return null; }
    }

    public string? GetString(string path, string? name) => GetValue(path, name)?.ToString();

    public int? GetInt(string path, string? name)
    {
        var v = GetValue(path, name);
        return v switch
        {
            int i => i,
            long l => (int)l,
            string s when int.TryParse(s, out var p) => p,
            _ => null
        };
    }

    public void SetValue(string path, string? name, object value, RegistryValueKind kind)
    {
        var (baseKey, sub) = Split(path);
        using var _ = baseKey;
        using var key = sub.Length == 0 ? baseKey : baseKey.CreateSubKey(sub, true);
        key?.SetValue(name ?? "", value, kind);
    }

    public void DeleteValue(string path, string? name)
    {
        try
        {
            using var key = Open(path, true);
            key?.DeleteValue(name ?? "", false);
        }
        catch { }
    }

    public void DeleteKey(string path)
    {
        var (baseKey, sub) = Split(path);
        using var _ = baseKey;
        if (sub.Length == 0) return;
        try { baseKey.DeleteSubKeyTree(sub, false); }
        catch { }
    }

    public RegistryValueSnapshot Snapshot(string path, string? name)
    {
        var snap = new RegistryValueSnapshot { Path = path, Name = name, KeyExisted = KeyExists(path) };
        if (!snap.KeyExisted) return snap;

        try
        {
            using var key = Open(path, false);
            if (key is null) return snap;

            var value = key.GetValue(name ?? "", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value is null) return snap;

            snap.ValueExisted = true;
            snap.Kind = key.GetValueKind(name ?? "");
            snap.Data = Serialize(value, snap.Kind);
        }
        catch { }

        return snap;
    }

    public void Restore(RegistryValueSnapshot snapshot)
    {
        if (!snapshot.KeyExisted)
        {
            DeleteKey(snapshot.Path);
            return;
        }

        if (!snapshot.ValueExisted)
        {
            DeleteValue(snapshot.Path, snapshot.Name);
            return;
        }

        var value = Deserialize(snapshot.Data, snapshot.Kind);
        if (value is not null) SetValue(snapshot.Path, snapshot.Name, value, snapshot.Kind);
    }

    private static string? Serialize(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.Binary => Convert.ToBase64String((byte[])value),
        RegistryValueKind.MultiString => string.Join('\n', (string[])value),
        RegistryValueKind.DWord or RegistryValueKind.QWord =>
            Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        _ => value.ToString()
    };

    private static object? Deserialize(string? data, RegistryValueKind kind)
    {
        if (data is null) return null;
        try
        {
            return kind switch
            {
                RegistryValueKind.Binary => Convert.FromBase64String(data),
                RegistryValueKind.MultiString => data.Length == 0
                    ? Array.Empty<string>()
                    : data.Split('\n'),
                RegistryValueKind.DWord => (object)int.Parse(data, CultureInfo.InvariantCulture),
                RegistryValueKind.QWord => long.Parse(data, CultureInfo.InvariantCulture),
                _ => data
            };
        }
        catch { return null; }
    }

    public async Task<bool> ExportKeyAsync(string path, string file, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var r = await _process.RunAsync("reg.exe", $"export \"{Normalize(path)}\" \"{file}\" /y /reg:64", ct)
            .ConfigureAwait(false);
        return r.Ok && File.Exists(file);
    }

    public async Task<bool> ImportKeyAsync(string file, CancellationToken ct)
    {
        if (!File.Exists(file)) return false;
        var r = await _process.RunAsync("reg.exe", $"import \"{file}\" /reg:64", ct).ConfigureAwait(false);
        return r.Ok;
    }

    private static string Normalize(string path)
    {
        var raw = path.Replace('/', '\\').TrimStart('\\');
        var idx = raw.IndexOf('\\');
        var root = (idx < 0 ? raw : raw[..idx]).ToUpperInvariant();
        var sub = idx < 0 ? "" : raw[idx..];

        var full = root switch
        {
            "HKLM" => "HKEY_LOCAL_MACHINE",
            "HKCU" => "HKEY_CURRENT_USER",
            "HKCR" => "HKEY_CLASSES_ROOT",
            "HKU" => "HKEY_USERS",
            "HKCC" => "HKEY_CURRENT_CONFIG",
            _ => root
        };
        return full + sub;
    }
}
