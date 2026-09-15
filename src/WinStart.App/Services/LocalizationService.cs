using System.Globalization;
using System.IO;
using System.Text.Json;
using WinStart.Core.Abstractions;

namespace WinStart.App.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly Dictionary<string, Dictionary<string, string>> _catalogs = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _current;

    public string Language { get; private set; } = "";

    public event EventHandler? LanguageChanged;

    public LocalizationService()
    {
        Load("ru");
        Load("en");
        _current = _catalogs["ru"];
    }

    private void Load(string language)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Localization", $"{language}.json");
            if (!File.Exists(path)) { _catalogs[language] = new Dictionary<string, string>(); return; }

            var json = File.ReadAllText(path);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            _catalogs[language] = dict;
        }
        catch
        {
            _catalogs[language] = new Dictionary<string, string>();
        }
    }

    public string this[string key]
    {
        get => _current.TryGetValue(key, out var value) ? value : key;
    }

    public string Format(string key, params object[] args)
    {
        var template = this[key];
        try { return string.Format(CultureInfo.CurrentUICulture, template, args); }
        catch { return template; }
    }

    public string Plural(string key, long count)
    {
        var form = Language == "en" ? (count == 1 ? "one" : "many") : RussianForm(count);
        return Format($"{key}.{form}", count);
    }

    private static string RussianForm(long n)
    {
        n = Math.Abs(n);
        var mod10 = n % 10;
        var mod100 = n % 100;

        if (mod10 == 1 && mod100 != 11) return "one";
        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14) return "few";
        return "many";
    }

    public void SetLanguage(string language)
    {
        if (!_catalogs.ContainsKey(language)) language = "ru";
        if (language == Language) return;

        Language = language;
        _current = _catalogs[language];

        var culture = CultureInfo.GetCultureInfo(language == "en" ? "en-US" : "ru-RU");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }
}
