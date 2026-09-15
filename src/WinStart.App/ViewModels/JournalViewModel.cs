using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.App.ViewModels;

public sealed partial class JournalEntryViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public JournalEntry Entry { get; }

    public JournalEntryViewModel(JournalEntry entry, ILocalizationService loc)
    {
        Entry = entry;
        _loc = loc;
    }

    public string Title => _loc[Entry.TitleKey];
    public string Timestamp => Entry.Timestamp.LocalDateTime.ToString("dd.MM.yyyy HH:mm:ss");

    public string DirectionText =>
        Entry.Direction == TweakDirection.Apply ? _loc["journal.apply"] : _loc["journal.revertDir"];

    public string StatusText => Entry.Status switch
    {
        TweakStatus.Success => _loc["journal.status.success"],
        TweakStatus.Failed => _loc["journal.status.failed"],
        _ => _loc["journal.status.skipped"]
    };

    public string StatusColor => Entry.Status switch
    {
        TweakStatus.Success => "#2FA65A",
        TweakStatus.Failed => "#E0483E",
        _ => "#C88A2E"
    };

    public bool CanRevert => Entry is { Reversible: true, Reverted: false, Status: TweakStatus.Success,
        Direction: TweakDirection.Apply };
    public bool IsReverted => Entry.Reverted;
    public string? Message => Entry.Message;
    public string Log => string.Join(Environment.NewLine, Entry.Log);
    public bool HasLog => Entry.Log.Count > 0;

    public void RefreshTexts()
    {
        foreach (var n in new[] { nameof(Title), nameof(DirectionText), nameof(StatusText) })
            OnPropertyChanged(n);
    }
}

public sealed partial class JournalViewModel : ObservableObject
{
    private readonly IJournalService _journal;
    private readonly ITweakService _tweaks;
    private readonly ILocalizationService _loc;
    private readonly IDialogService _dialogs;
    private readonly IBusyService _busy;
    private readonly IPathProvider _paths;

    public ObservableCollection<JournalEntryViewModel> Entries { get; } = [];

    public JournalViewModel(IJournalService journal, ITweakService tweaks,
        ILocalizationService loc, IDialogService dialogs, IBusyService busy, IPathProvider paths)
    {
        _journal = journal;
        _tweaks = tweaks;
        _loc = loc;
        _dialogs = dialogs;
        _busy = busy;
        _paths = paths;

        _journal.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.Invoke(Reload);
    }

    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string? _exportStatus;

    public string Title => _loc["journal.title"];
    public string EmptyText => _loc["journal.empty"];

    public void Reload()
    {
        Entries.Clear();
        foreach (var entry in _journal.Entries.OrderByDescending(e => e.Timestamp))
            Entries.Add(new JournalEntryViewModel(entry, _loc));

        IsEmpty = Entries.Count == 0;
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(EmptyText));
        foreach (var e in Entries) e.RefreshTexts();
    }

    [RelayCommand]
    private async Task RevertAsync(JournalEntryViewModel? vm)
    {
        if (vm is null || !vm.CanRevert || _busy.IsBusy) return;

        using var _ = _busy.Begin();
        await _tweaks.RevertEntryAsync(vm.Entry, CancellationToken.None);
    }

    [RelayCommand]
    private async Task ClearAsync()
    {
        var ok = await _dialogs.ConfirmAsync(_loc["journal.clear"], _loc["journal.clear.confirm"]);
        if (ok) await _journal.ClearAsync();
    }

    [RelayCommand]
    private void OpenFile()
    {
        try
        {
            if (File.Exists(_journal.HistoryFile))
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_journal.HistoryFile}\"")
                    { UseShellExecute = true });
        }
        catch { }
    }

    [RelayCommand]
    private void Export()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"WinStart-{_loc["journal.export.name"]}-{DateTime.Now:yyyy-MM-dd}.zip",
            DefaultExt = ".zip",
            Filter = "ZIP|*.zip",
            Title = _loc["journal.export"]
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            if (File.Exists(dialog.FileName)) File.Delete(dialog.FileName);
            using var zip = System.IO.Compression.ZipFile.Open(dialog.FileName, System.IO.Compression.ZipArchiveMode.Create);

            if (File.Exists(_journal.HistoryFile))
                zip.CreateEntryFromFile(_journal.HistoryFile, Path.GetFileName(_journal.HistoryFile));

            foreach (var (folder, name) in new[] { (_paths.Backups, "backups"), (_paths.Logs, "logs") })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    zip.CreateEntryFromFile(file, Path.Combine(name, Path.GetRelativePath(folder, file)).Replace('\\', '/'));
            }

            ExportStatus = _loc.Format("journal.export.saved", dialog.FileName);
        }
        catch (Exception ex)
        {
            ExportStatus = ex.Message;
        }
    }
}
