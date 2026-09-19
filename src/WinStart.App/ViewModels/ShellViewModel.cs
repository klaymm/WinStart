using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Wpf.Ui.Controls;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.App.ViewModels;

public sealed partial class NavItemViewModel : ObservableObject
{
    private readonly string _titleKey;
    private readonly Func<object> _contentFactory;
    private readonly ILocalizationService _loc;
    private object? _content;

    public NavItemViewModel(string titleKey, SymbolRegular icon, Func<object> contentFactory,
        ILocalizationService loc)
    {
        _titleKey = titleKey;
        Icon = icon;
        _contentFactory = contentFactory;
        _loc = loc;
    }

    public SymbolRegular Icon { get; }
    public string TitleKey => _titleKey;
    public string Title => _loc[_titleKey];

    [ObservableProperty] private bool _isSelected;

    public object Content => _content ??= _contentFactory();

    public bool IsMaterialized => _content is not null;

    public void RefreshTitle() => OnPropertyChanged(nameof(Title));
}

public sealed partial class ShellViewModel : ObservableObject
{
    internal static readonly (TweakCategory Category, string TitleKey, SymbolRegular Icon)[] CategoryNav =
    [
        (TweakCategory.Interface, "nav.interface", SymbolRegular.DesktopMac24),
        (TweakCategory.ContextMenu, "nav.contextMenu", SymbolRegular.ContentView24),
        (TweakCategory.Cleaning, "nav.cleaning", SymbolRegular.Broom24),
        (TweakCategory.Optimization, "nav.optimization", SymbolRegular.Flash24),
        (TweakCategory.Apps, "nav.apps", SymbolRegular.Box24),
        (TweakCategory.Components, "nav.components", SymbolRegular.PuzzlePiece24),
        (TweakCategory.Misc, "nav.misc", SymbolRegular.MoreHorizontal24)
    ];

    private readonly IServiceProvider _services;
    private readonly ILocalizationService _loc;
    private readonly IBusyService _busy;
    private readonly ISystemInfoService _systemInfo;
    private readonly IDialogService _dialogs;
    private readonly SearchViewModel _search;
    private NavItemViewModel? _beforeSearch;
    private bool _closingSearch;

    public ShellViewModel(IServiceProvider services, ILocalizationService loc, IBusyService busy,
        ISystemInfoService systemInfo, IDialogService dialogs, SearchViewModel search)
    {
        _services = services;
        _loc = loc;
        _busy = busy;
        _systemInfo = systemInfo;
        _dialogs = dialogs;
        _search = search;
        _search.Attach(() => TopItems.Concat(BottomItems), Select);

        _busy.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            OnPropertyChanged(nameof(CanNavigate)));

        BuildNavigation();
        Select(TopItems[0]);
        _ = LoadProfileAsync();
    }

    // ---- Поиск по всей программе ----
    [ObservableProperty] private string _searchText = "";

    public bool IsSearching => CurrentContent == _search;

    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            _search.Query = value;
            if (IsSearching) return;

            _beforeSearch = TopItems.Concat(BottomItems).FirstOrDefault(n => n.IsSelected);
            foreach (var n in TopItems.Concat(BottomItems)) n.IsSelected = false;
            CurrentContent = _search;
            OnPropertyChanged(nameof(IsSearching));
            return;
        }

        if (!IsSearching || _closingSearch) return;
        _search.Query = "";
        Select(_beforeSearch ?? TopItems[0]);
    }

    public List<NavItemViewModel> TopItems { get; } = [];
    public List<NavItemViewModel> BottomItems { get; } = [];

    public string Title => _loc["app.title"];

    public bool CanNavigate => !_busy.IsBusy;

    [ObservableProperty] private object? _currentContent;

    // ---- Профиль ----
    [ObservableProperty] private string _profileName = Environment.UserName;
    [ObservableProperty] private string _profileRole = "";
    [ObservableProperty] private string _profileInitial = "?";

    private async Task LoadProfileAsync()
    {
        try
        {
            var profile = new ProfileViewModel(await _systemInfo.GetSummaryAsync(CancellationToken.None), _loc);
            ProfileName = profile.Name;
            ProfileRole = profile.Role;
            ProfileInitial = profile.Initial;
        }
        catch { }
    }

    [RelayCommand]
    private async Task OpenProfileAsync()
    {
        var s = await _systemInfo.GetSummaryAsync(CancellationToken.None);
        await _dialogs.ShowProfileAsync(new ProfileViewModel(s, _loc));
    }

    private void BuildNavigation()
    {
        TopItems.Add(new NavItemViewModel("nav.home", SymbolRegular.Home24,
            () => _services.GetRequiredService<HomeViewModel>(), _loc));

        foreach (var (category, titleKey, icon) in CategoryNav)
        {
            TopItems.Add(new NavItemViewModel(titleKey, icon, () =>
            {
                var vm = _services.GetRequiredService<CategoryPageViewModel>();
                vm.Load(category, titleKey);
                return vm;
            }, _loc));
        }

        var afterApps = TopItems.FindIndex(i => i.TitleKey == "nav.apps") + 1;
        TopItems.Insert(afterApps, new NavItemViewModel("nav.programs", SymbolRegular.AppsAddIn24,
            () => _services.GetRequiredService<ProgramsViewModel>(), _loc));
        TopItems.Insert(afterApps + 1, new NavItemViewModel("nav.startup", SymbolRegular.Rocket24,
            () => _services.GetRequiredService<StartupViewModel>(), _loc));
        TopItems.Insert(afterApps + 2, new NavItemViewModel("nav.services", SymbolRegular.Wrench24,
            () => _services.GetRequiredService<ServicesViewModel>(), _loc));
        TopItems.Insert(afterApps + 3, new NavItemViewModel("nav.network", SymbolRegular.Globe24,
            () => _services.GetRequiredService<NetworkViewModel>(), _loc));

        TopItems.Add(new NavItemViewModel("nav.journal", SymbolRegular.History24,
            () => _services.GetRequiredService<JournalViewModel>(), _loc));

        TopItems.Add(new NavItemViewModel("nav.updates", SymbolRegular.Sparkle24,
            () => _services.GetRequiredService<UpdatesViewModel>(), _loc));

        BottomItems.Add(new NavItemViewModel("nav.unattend", SymbolRegular.DocumentSettings20,
            () => _services.GetRequiredService<UnattendViewModel>(), _loc));

        BottomItems.Add(new NavItemViewModel("nav.settings", SymbolRegular.Settings24,
            () => _services.GetRequiredService<SettingsViewModel>(), _loc));
    }

    [RelayCommand]
    private void Select(NavItemViewModel? item)
    {
        if (item is null) return;

        if (IsSearching)
        {
            _closingSearch = true;
            SearchText = "";
            _search.Query = "";
            _closingSearch = false;
        }

        foreach (var n in TopItems.Concat(BottomItems)) n.IsSelected = false;
        item.IsSelected = true;

        var content = item.Content;
        CurrentContent = content;
        OnPropertyChanged(nameof(IsSearching));

        switch (content)
        {
            case HomeViewModel home when !home.IsLoaded:
                _ = home.LoadAsync();
                break;
            case ProgramsViewModel programs when !programs.IsLoaded:
                _ = programs.RefreshAsync();
                break;
            case StartupViewModel startup when !startup.IsLoaded:
                _ = startup.LoadAsync();
                break;
            case NetworkViewModel network when !network.IsLoaded:
                _ = network.LoadAsync();
                break;
            case ServicesViewModel services when !services.IsLoaded:
                _ = services.LoadAsync();
                break;
            case JournalViewModel journal:
                journal.Reload();
                break;
            case CategoryPageViewModel category:
                _ = category.RefreshAllAsync();
                break;
        }
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        _search.RefreshTexts();

        foreach (var item in TopItems.Concat(BottomItems))
        {
            item.RefreshTitle();
            if (!item.IsMaterialized) continue;

            switch (item.Content)
            {
                case HomeViewModel h: h.RefreshTexts(); break;
                case CategoryPageViewModel c: c.RefreshTexts(); break;
                case JournalViewModel j: j.RefreshTexts(); break;
                case UpdatesViewModel u: u.RefreshTexts(); break;
                case ProgramsViewModel p: p.RefreshTexts(); break;
                case StartupViewModel st: st.RefreshTexts(); break;
                case NetworkViewModel net: net.RefreshTexts(); break;
                case ServicesViewModel svc: svc.RefreshTexts(); break;
                case UnattendViewModel n: n.RefreshTexts(); break;
            }
        }

        _ = LoadProfileAsync();
    }
}
