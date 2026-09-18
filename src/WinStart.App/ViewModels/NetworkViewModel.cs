using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Services;

namespace WinStart.App.ViewModels;

public sealed class DnsProviderViewModel(DnsProvider provider, ILocalizationService loc, Action<DnsProviderViewModel> select)
    : ObservableObject
{
    private bool _isSelected;

    public DnsProvider Provider { get; } = provider;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value) select(this);
            else SetProperty(ref _isSelected, false);
        }
    }

    internal void SetSelected(bool value) => SetProperty(ref _isSelected, value, nameof(IsSelected));
    public string Title => loc[$"network.dns.{Provider}"];
    public string Servers
    {
        get
        {
            var (v4, v6) = NetworkService.AddressesOf(Provider);
            return v4.Length == 0 ? loc["network.dns.automatic.desc"] : string.Join("  ·  ", v4.Concat(v6));
        }
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Servers));
    }
}

public sealed partial class NetworkViewModel : ObservableObject
{
    private readonly INetworkService _network;
    private readonly ILocalizationService _loc;
    private readonly IBusyService _busy;

    public NetworkViewModel(INetworkService network, ILocalizationService loc, IBusyService busy)
    {
        _network = network;
        _loc = loc;
        _busy = busy;

        Providers = Enum.GetValues<DnsProvider>()
            .OrderBy(p => p == DnsProvider.Automatic)
            .Select(p => new DnsProviderViewModel(p, loc, Select))
            .ToList();

        _selectedProvider = Providers.First(p => p.Provider == DnsProvider.Google);
        _selectedProvider.SetSelected(true);
    }

    public ObservableCollection<NetworkAdapter> Adapters { get; } = [];
    public IReadOnlyList<DnsProviderViewModel> Providers { get; }

    public string Title => _loc["nav.network"];
    public string Subtitle => _loc["network.subtitle"];

    [ObservableProperty] private NetworkAdapter? _selectedAdapter;
    [ObservableProperty] private DnsProviderViewModel _selectedProvider;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private bool _statusIsError;

    public string CurrentIpv4 => SelectedAdapter is { Ipv4Dns.Length: > 0 } a ? a.Ipv4Dns : _loc["network.dns.auto"];
    public string CurrentIpv6 => SelectedAdapter is { Ipv6Dns.Length: > 0 } a ? a.Ipv6Dns : _loc["network.dns.auto"];

    private void Select(DnsProviderViewModel provider)
    {
        SelectedProvider = provider;
        foreach (var p in Providers) p.SetSelected(p == provider);
    }

    partial void OnSelectedAdapterChanged(NetworkAdapter? value)
    {
        OnPropertyChanged(nameof(CurrentIpv4));
        OnPropertyChanged(nameof(CurrentIpv6));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value) => ApplyCommand.NotifyCanExecuteChanged();

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusText = _loc["network.loading"];
        try
        {
            var current = SelectedAdapter?.Index;
            var adapters = await _network.GetAdaptersAsync(CancellationToken.None);

            Adapters.Clear();
            foreach (var adapter in adapters) Adapters.Add(adapter);

            SelectedAdapter = Adapters.FirstOrDefault(a => a.Index == current)
                              ?? Adapters.FirstOrDefault(a => a.IsUp)
                              ?? Adapters.FirstOrDefault();
            StatusText = Adapters.Count == 0 ? _loc["network.none"] : null;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsLoaded = true;
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => LoadAsync();

    private bool CanApply() => SelectedAdapter is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (SelectedAdapter is not { } adapter) return;

        using var _ = _busy.Begin();
        IsBusy = true;
        StatusIsError = false;
        StatusText = _loc.Format("network.applying", adapter.Name);
        try
        {
            var error = await _network.SetDnsAsync(adapter, SelectedProvider.Provider, CancellationToken.None);
            if (error is null)
            {
                IsBusy = false;
                await LoadAsync();
                StatusIsError = false;
                StatusText = SelectedProvider.Provider == DnsProvider.Automatic
                    ? _loc.Format("network.reset", adapter.Name)
                    : _loc.Format("network.applied", SelectedProvider.Title, adapter.Name);
            }
            else
            {
                StatusIsError = true;
                StatusText = _loc.Format("network.failed", error);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CurrentIpv4));
        OnPropertyChanged(nameof(CurrentIpv6));
        foreach (var p in Providers) p.RefreshTexts();
    }
}
