using System.IO;
using System.Text.Json;
using WinStart.Core.Abstractions;

namespace WinStart.App.Services;

public enum AppTheme { System, Light, Dark }

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string Language { get; set; } = "ru";
    public bool ShowSplash { get; set; } = true;
    public bool ReceiveBetas { get; set; }
}

public interface ISettingsService
{
    AppSettings Current { get; }
    void Save();
}

public sealed class SettingsService : ISettingsService
{
    private readonly string _file;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; }

    public SettingsService(IPathProvider paths)
    {
        _file = Path.Combine(paths.UserData, "settings.json");
        var existed = File.Exists(_file);
        Current = Load();

        if (!existed) Save();
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_file))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_file)) ?? new AppSettings();
        }
        catch { }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch { }
    }
}
