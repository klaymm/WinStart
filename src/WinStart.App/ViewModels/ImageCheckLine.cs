using CommunityToolkit.Mvvm.ComponentModel;
using WinStart.Core.Abstractions;
using WinStart.Core.Unattend;

namespace WinStart.App.ViewModels;

public sealed class ImageCheckLine(ILocalizationService loc, ImageCheckResult result) : ObservableObject
{
    public ImageCheckLine(ILocalizationService loc, ImageCheckKind kind, string key, params object[] args)
        : this(loc, new ImageCheckResult(kind, key, args)) { }

    public ImageCheckKind Kind { get; } = result.Kind;
    public bool IsOk => Kind == ImageCheckKind.Ok;
    public bool IsWarn => Kind == ImageCheckKind.Warn;
    public string Text => result.Args.Length == 0 ? loc[result.Key] : loc.Format(result.Key, result.Args);

    public void RefreshTexts() => OnPropertyChanged(nameof(Text));
}
