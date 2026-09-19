using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;
using WinStart.Core.Tweaks;

namespace WinStart.App.ViewModels;

public sealed class SearchLink(string title, string? subtitle, Action open)
{
    public string Title { get; } = title;
    public string? Subtitle { get; } = subtitle;
    public IRelayCommand OpenCommand { get; } = new RelayCommand(open);
}

public sealed class SearchGroup(string title, IReadOnlyList<object> items)
{
    public string Title { get; } = title;
    public IReadOnlyList<object> Items { get; } = items;
}

public sealed partial class SearchViewModel : ObservableObject
{
    private readonly TweakRegistry _registry;
    private readonly ITweakService _tweaks;
    private readonly ILocalizationService _loc;
    private readonly IDialogService _dialogs;
    private readonly IBusyService _busy;
    private readonly IRestorePointService _restore;
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, TweakCardViewModel> _cards = [];

    private Func<IEnumerable<NavItemViewModel>> _navItems = () => [];
    private Action<NavItemViewModel> _open = _ => { };

    public SearchViewModel(TweakRegistry registry, ITweakService tweaks, ILocalizationService loc,
        IDialogService dialogs, IBusyService busy, IRestorePointService restore, IServiceProvider services)
    {
        _registry = registry;
        _tweaks = tweaks;
        _loc = loc;
        _dialogs = dialogs;
        _busy = busy;
        _restore = restore;
        _services = services;
    }

    public void Attach(Func<IEnumerable<NavItemViewModel>> navItems, Action<NavItemViewModel> open)
    {
        _navItems = navItems;
        _open = open;
    }

    public ObservableCollection<SearchGroup> Groups { get; } = [];

    [ObservableProperty] private string _query = "";
    [ObservableProperty] private int _count;

    public string Title => _loc["search.title"];
    public string Summary => Count == 0 ? _loc["search.empty"] : _loc.Plural("search.count", Count);

    partial void OnQueryChanged(string value) => Run();

    private bool Match(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(Query.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private NavItemViewModel? Nav(string titleKey) => _navItems().FirstOrDefault(n => n.TitleKey == titleKey);

    private void AddGroup(string title, List<object> items, ref int total)
    {
        if (items.Count == 0) return;
        total += items.Count;
        Groups.Add(new SearchGroup(title, items));
    }

    private void Run()
    {
        Groups.Clear();
        var q = Query.Trim();
        if (q.Length == 0)
        {
            Count = 0;
            OnPropertyChanged(nameof(Summary));
            return;
        }

        var total = 0;

        var home = _services.GetRequiredService<HomeViewModel>();
        var homeItem = Nav("nav.home");
        if (!home.IsLoaded) LoadThenRerun(home.LoadAsync());
        else
        {
            var stats = home.Stats
                .Where(st => Match(st.Title) || Match(st.Value) || Match(st.Extra))
                .Select(st => (object)new SearchLink(st.Title, st.Extra is null ? st.Value : $"{st.Value} · {st.Extra}",
                    () => { if (homeItem is not null) _open(homeItem); }))
                .ToList();
            if (Match(_loc["home.report"]))
                stats.Add(new SearchLink(_loc["home.report"], _loc["home.report.title"], () => { if (homeItem is not null) _open(homeItem); }));
            AddGroup(_loc["nav.home"], stats, ref total);
        }

        foreach (var (category, titleKey, _) in ShellViewModel.CategoryNav)
        {
            var hits = _registry.ByCategory(category)
                .Where(d => Match(_loc[d.TitleKey]) || Match(_loc[d.DescriptionKey]))
                .Select(Card)
                .ToList();
            if (hits.Count == 0) continue;

            total += hits.Count;
            Groups.Add(new SearchGroup(_loc[titleKey], hits));
            foreach (var card in hits) _ = card.RefreshStateAsync();
        }

        var programs = _services.GetRequiredService<ProgramsViewModel>();
        var programsItem = Nav("nav.programs");
        var apps = programs.All
            .Where(a => Match(a.Name) || Match(a.Id) || Match(a.Description))
            .Select(a => (object)new SearchLink(a.Name, a.IsInstalled ? $"{a.Description} · {a.InstalledText}" : a.Description, () =>
            {
                if (a.IsInstalled) programs.OnlyNotInstalled = false;
                programs.Filter = a.Name;
                if (programsItem is not null) _open(programsItem);
            }))
            .ToList();
        AddGroup(_loc["nav.programs"], apps, ref total);

        var startup = _services.GetRequiredService<StartupViewModel>();
        var startupItem = Nav("nav.startup");
        if (!startup.IsLoaded) LoadThenRerun(startup.LoadAsync());
        else
        {
            var entries = startup.AllEntries
                .Where(e => Match(e.Name) || Match(e.Command) || Match(e.Entry.Publisher) || Match(e.Location))
                .Select(e => (object)new SearchLink(e.Name, $"{e.Publisher} · {e.Location}", () =>
                {
                    startup.Filter = q;
                    if (e.IsMicrosoft) startup.HideMicrosoft = false;
                    if (startupItem is not null) _open(startupItem);
                }))
                .ToList();
            AddGroup(_loc["nav.startup"], entries, ref total);
        }

        var journalItem = Nav("nav.journal");
        var journal = _services.GetRequiredService<IJournalService>().Entries
            .Where(e => Match(_loc[e.TitleKey]) || Match(e.Message))
            .OrderByDescending(e => e.Timestamp)
            .Take(20)
            .Select(e => (object)new SearchLink(_loc[e.TitleKey], e.Timestamp.LocalDateTime.ToString("dd.MM.yyyy HH:mm"),
                () => { if (journalItem is not null) _open(journalItem); }))
            .ToList();
        AddGroup(_loc["nav.journal"], journal, ref total);

        var updatesItem = Nav("nav.updates");
        var changes = _services.GetRequiredService<UpdatesViewModel>().Entries
            .SelectMany(e => e.Changes.Where(Match).Select(c => (object)new SearchLink(e.Title, c,
                () => { if (updatesItem is not null) _open(updatesItem); })))
            .ToList();
        AddGroup(_loc["nav.updates"], changes, ref total);

        var unattend = _services.GetRequiredService<UnattendViewModel>();
        var unattendItem = Nav("nav.unattend");
        var sections = new List<object>();
        foreach (var section in unattend.Sections)
        {
            var matched = Descendants(section).Where(i => Match(i.Title) || Match(i.Description)).ToList();
            if (matched.Count == 0 && !Match(section.Title)) continue;

            var preview = string.Join(" · ", matched.Select(i => i.Title).Where(t => t.Length > 0).Distinct().Take(4));
            sections.Add(new SearchLink(section.Title, preview.Length == 0 ? null : preview, () =>
            {
                unattend.FilterText = q;
                if (unattendItem is not null) _open(unattendItem);
            }));
        }
        AddGroup(_loc["nav.unattend"], sections, ref total);

        var settingsItem = Nav("nav.settings");
        var settings = SettingsKeys
            .Where(k => Match(_loc[k.Title]) || (k.Description is not null && Match(_loc[k.Description])))
            .Select(k => (object)new SearchLink(_loc[k.Title], k.Description is null ? null : _loc[k.Description],
                () => { if (settingsItem is not null) _open(settingsItem); }))
            .ToList();
        AddGroup(_loc["nav.settings"], settings, ref total);

        var pages = _navItems()
            .Where(n => Match(n.Title))
            .Select(n => (object)new SearchLink(n.Title, null, () => _open(n)))
            .ToList();
        AddGroup(_loc["search.pages"], pages, ref total);

        Count = total;
        OnPropertyChanged(nameof(Summary));
    }

    private static readonly (string Title, string? Description)[] SettingsKeys =
    [
        ("settings.theme", null),
        ("settings.language", null),
        ("settings.animation", "settings.animation.desc"),
        ("settings.betas", "settings.betas.desc"),
        ("restore.title", "restore.desc"),
        ("settings.openLogs", null),
        ("settings.openBackups", null)
    ];

    private bool _rerunPending;

    private void LoadThenRerun(Task load)
    {
        if (_rerunPending) return;
        _rerunPending = true;
        load.ContinueWith(_ =>
        {
            _rerunPending = false;
            if (Query.Trim().Length > 0) Run();
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static IEnumerable<FormItem> Descendants(FormItem item)
    {
        foreach (var child in item.Children)
        {
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    private TweakCardViewModel Card(TweakDefinition def)
    {
        if (_cards.TryGetValue(def.Id, out var card)) return card;
        card = new TweakCardViewModel(def, _tweaks, _loc, _dialogs, _busy, _restore);
        _cards[def.Id] = card;
        return card;
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        foreach (var card in _cards.Values) card.RefreshTexts();
        Run();
    }
}
