using System.Windows.Controls;
using Wpf.Ui.Controls;
using WinStart.App.Infrastructure;
using WinStart.App.ViewModels;
using WinStart.Core.Abstractions;

namespace WinStart.App.Services;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string message);
    Task ShowProfileAsync(ProfileViewModel profile);
    Task ShowTextAsync(string title, string text);
    void SetContentPresenter(ContentPresenter presenter);
}

public sealed class DialogService : IDialogService
{
    private readonly ILocalizationService _loc;
    private ContentPresenter? _presenter;

    public DialogService(ILocalizationService loc) => _loc = loc;

    public void SetContentPresenter(ContentPresenter presenter) => _presenter = presenter;

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_presenter is null) return false;

        var dialog = new ContentDialog(_presenter)
        {
            Title = title,
            Content = message,
            PrimaryButtonText = _loc["dialog.yes"],
            CloseButtonText = _loc["dialog.no"],
            DefaultButton = ContentDialogButton.Close
        };
        DialogMotion.Attach(dialog);

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowProfileAsync(ProfileViewModel profile)
    {
        if (_presenter is null) return;

        var dialog = new ContentDialog(_presenter)
        {
            Content = new Controls.ProfileCard { DataContext = profile },
            CloseButtonText = _loc["dialog.close"]
        };
        DialogMotion.Attach(dialog);

        await dialog.ShowAsync();
    }

    public async Task ShowTextAsync(string title, string text)
    {
        if (_presenter is null) return;

        var dialog = new ContentDialog(_presenter)
        {
            Title = title,
            Content = new Controls.TextPreview(text),
            CloseButtonText = _loc["dialog.close"],
            DialogMaxWidth = 900,
            DialogMaxHeight = 700
        };
        DialogMotion.Attach(dialog);

        await dialog.ShowAsync();
    }
}
