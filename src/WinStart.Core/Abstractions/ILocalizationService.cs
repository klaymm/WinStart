namespace WinStart.Core.Abstractions;

public interface ILocalizationService
{
    string Language { get; }
    event EventHandler? LanguageChanged;

    string this[string key] { get; }
    string Format(string key, params object[] args);

    string Plural(string key, long count);

    void SetLanguage(string language);
}
