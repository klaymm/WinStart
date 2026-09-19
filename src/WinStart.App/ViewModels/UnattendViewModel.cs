using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Schneegans.Unattend;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Unattend;

namespace WinStart.App.ViewModels;

public sealed partial class UnattendViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ILocalizationService _loc;
    private readonly IDialogService _dialogs;
    private readonly IImageCheckService _images;
    private readonly string _file;
    private UnattendSettings _s;
    private FormContext _ctx = null!;
    private List<FormToggle> _wingetToggles = [];

    public UnattendViewModel(ILocalizationService loc, IPathProvider paths, IDialogService dialogs, IImageCheckService images)
    {
        _loc = loc;
        _dialogs = dialogs;
        _images = images;
        _file = Path.Combine(paths.UserData, "unattend.json");
        _s = Load();
        _sections = Build();
        RefreshDrives();
    }

    public string Title => _loc["nav.unattend"];
    public string Subtitle => _loc["ua.subtitle"];

    [ObservableProperty] private IReadOnlyList<FormSection> _sections;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private bool _statusIsError;

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        foreach (var s in Sections) s.RefreshTexts();
        foreach (var l in ImageLines) l.RefreshTexts();
    }

    // ---------------------------------------------------------------- поиск по форме

    [ObservableProperty] private string _filterText = "";

    [ObservableProperty] private bool _nothingFound;

    public bool IsFiltering => !string.IsNullOrWhiteSpace(FilterText);

    partial void OnFilterTextChanged(string value)
    {
        var any = false;
        foreach (var s in Sections) any |= s.ApplyFilter(value);
        NothingFound = IsFiltering && !any;
        OnPropertyChanged(nameof(IsFiltering));
    }

    [RelayCommand]
    private void ClearFilter() => FilterText = "";

    // ---------------------------------------------------------------- команды

    private bool TryBuild(out byte[] xml)
    {
        try
        {
            xml = UnattendBuilder.Build(_s);
            return true;
        }
        catch (Exception ex)
        {
            xml = [];
            StatusIsError = true;
            StatusText = ex.Message;
            return false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!TryBuild(out var xml)) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "autounattend.xml",
            DefaultExt = ".xml",
            Filter = "autounattend.xml|*.xml",
            Title = _loc["ua.save"]
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            File.WriteAllBytes(dialog.FileName, xml);
            StatusIsError = false;
            StatusText = _loc.Format("ua.saved", dialog.FileName);
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusText = ex.Message;
        }
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!TryBuild(out var xml)) return;
        await _dialogs.ShowTextAsync(_loc["ua.preview"], Encoding.UTF8.GetString(xml).TrimStart('\uFEFF'));
    }

    // ---------------------------------------------------------------- проверка образа и флешка

    [ObservableProperty] private string _imagePath = "";
    [ObservableProperty] private bool _isCheckingImage;
    public ObservableCollection<ImageCheckLine> ImageLines { get; } = [];

    public ObservableCollection<RemovableDrive> Drives { get; } = [];
    public bool HasDrives => Drives.Count > 0;
    [ObservableProperty] private RemovableDrive? _selectedDrive;
    [ObservableProperty] private string? _driveStatus;
    [ObservableProperty] private bool _driveStatusIsError;

    partial void OnImagePathChanged(string value) => CheckImageCommand.NotifyCanExecuteChanged();

    partial void OnSelectedDriveChanged(RemovableDrive? value)
    {
        CheckDriveCommand.NotifyCanExecuteChanged();
        WriteToDriveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "ISO, WIM, ESD|*.iso;*.wim;*.esd|*.*|*.*",
            Title = _loc["ua.check.browse"]
        };
        if (dialog.ShowDialog() == true) ImagePath = dialog.FileName;
    }

    private bool CanCheckImage() => !IsCheckingImage && !string.IsNullOrWhiteSpace(ImagePath);

    [RelayCommand(CanExecute = nameof(CanCheckImage))]
    private Task CheckImageAsync() => CheckAsync(ImagePath);

    private bool HasDrive() => SelectedDrive is not null && !IsCheckingImage;

    [RelayCommand(CanExecute = nameof(HasDrive))]
    private Task CheckDriveAsync() => CheckAsync(SelectedDrive!.Root);

    private async Task CheckAsync(string path)
    {
        IsCheckingImage = true;
        NotifyImageCommands();
        ImageLines.Clear();
        ImageLines.Add(new ImageCheckLine(_loc, ImageCheckKind.Info, "ua.check.reading", path));
        try
        {
            var images = await _images.InspectAsync(path, CancellationToken.None);
            ImageLines.Clear();
            foreach (var line in ImageCheck.Compare(_s, images)) ImageLines.Add(new ImageCheckLine(_loc, line));
        }
        catch (ImageCheckException ex)
        {
            ImageLines.Clear();
            ImageLines.Add(ex.Kind switch
            {
                ImageCheckError.NoInstallImage => new ImageCheckLine(_loc, ImageCheckKind.Warn, "ua.check.noWim"),
                ImageCheckError.MountFailed => new ImageCheckLine(_loc, ImageCheckKind.Warn, "ua.check.noMount"),
                _ => new ImageCheckLine(_loc, ImageCheckKind.Warn, "ua.check.error", ex.Message)
            });
        }
        catch (Exception ex)
        {
            ImageLines.Clear();
            ImageLines.Add(new ImageCheckLine(_loc, ImageCheckKind.Warn, "ua.check.error", ex.Message));
        }
        finally
        {
            IsCheckingImage = false;
            NotifyImageCommands();
        }
    }

    private void NotifyImageCommands()
    {
        CheckImageCommand.NotifyCanExecuteChanged();
        CheckDriveCommand.NotifyCanExecuteChanged();
        WriteToDriveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void RefreshDrives()
    {
        var current = SelectedDrive?.Root;
        Drives.Clear();
        foreach (var d in _images.RemovableDrives()) Drives.Add(d);
        SelectedDrive = Drives.FirstOrDefault(d => d.Root == current) ?? Drives.FirstOrDefault();
        OnPropertyChanged(nameof(HasDrives));
        DriveStatus = null;
    }

    [RelayCommand(CanExecute = nameof(HasDrive))]
    private async Task WriteToDriveAsync()
    {
        if (SelectedDrive is null || !TryBuild(out var xml)) return;

        var target = Path.Combine(SelectedDrive.Root, "autounattend.xml");
        if (File.Exists(target) &&
            !await _dialogs.ConfirmAsync(_loc["ua.usb.write"], _loc.Format("ua.usb.overwrite", target)))
            return;

        try
        {
            await File.WriteAllBytesAsync(target, xml);
            DriveStatusIsError = false;
            DriveStatus = _loc.Format("ua.usb.written", target);
        }
        catch (Exception ex)
        {
            DriveStatusIsError = true;
            DriveStatus = ex.Message;
        }
    }

    [RelayCommand]
    private async Task ResetAsync()
    {
        var ok = await _dialogs.ConfirmAsync(_loc["ua.reset"], _loc["ua.reset.confirm"]);
        if (!ok) return;

        _s = new UnattendSettings();
        Sections = Build();
        Commit();
    }

    public void SetWingetApps(IEnumerable<string> ids)
    {
        _s.WingetApps = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        foreach (var t in _wingetToggles) t.Refresh();
        Commit();
    }

    // ---------------------------------------------------------------- хранение

    private void Commit()
    {
        StatusText = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_s, JsonOptions));
        }
        catch { }
    }

    private UnattendSettings Load()
    {
        try
        {
            if (File.Exists(_file))
            {
                var node = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(_file)) as System.Text.Json.Nodes.JsonObject;
                var loaded = node is not null && UnattendSettings.TryMigrate(node)
                    ? node.Deserialize<UnattendSettings>(JsonOptions)
                    : null;
                if (loaded is { IsCurrentVersion: true })
                {
                    if (loaded.WingetApps.Remove("dotPDNLLC.paintdotnet")) loaded.WingetApps.Add("dotPDN.PaintDotNet");
                    return loaded;
                }
            }
        }
        catch { }

        return new UnattendSettings();
    }

    // ---------------------------------------------------------------- построение формы

    private IReadOnlyList<FormSection> Build()
    {
        _ctx = new FormContext(_loc, Commit);
        var s = _s;

        // ---- Регион и язык ----
        var keyboard1 = Sel("ua.lang.keyboard", Cat(UnattendCatalog.Keyboards), () => s.Keyboard1, v => s.Keyboard1 = v);
        var keyboard2 = Sel("ua.lang.keyboard", Cat(UnattendCatalog.Keyboards), () => s.Keyboard2, v => s.Keyboard2 = v);
        var keyboard3 = Sel("ua.lang.keyboard", Cat(UnattendCatalog.Keyboards), () => s.Keyboard3, v => s.Keyboard3 = v);
        var geo = Sel("ua.lang.geo", Cat(UnattendCatalog.GeoLocations), () => s.GeoLocation, v => s.GeoLocation = v);

        void LocaleChanged(string locale, Action<string> setKeyboard, FormSelect keyboard, bool first)
        {
            if (UnattendCatalog.DefaultKeyboardFor(locale) is { } kb) { setKeyboard(kb); keyboard.Refresh(); }
            if (first && UnattendCatalog.DefaultGeoLocationFor(locale) is { } g) { s.GeoLocation = g; geo.Refresh(); }
        }

        var language = Sec("ua.s.language",
        [
            Choice(() => s.LanguageInteractive ? "i" : "u", v => s.LanguageInteractive = v == "i",
                Opt("u", "ua.lang.unattended", "ua.lang.unattended.desc",
                [
                    Sel("ua.lang.display", Cat(UnattendCatalog.ImageLanguages), () => s.DisplayLanguage, v => s.DisplayLanguage = v),
                    Sel("ua.lang.locale1", Cat(UnattendCatalog.UserLocales), () => s.Locale1,
                        v => { s.Locale1 = v; LocaleChanged(v, k => s.Keyboard1 = k, keyboard1, first: true); }, "ua.lang.locale1.desc"),
                    keyboard1,
                    Toggle("ua.lang.second", () => s.UseLocale2, v => s.UseLocale2 = v, children:
                    [
                        Sel("ua.lang.locale", Cat(UnattendCatalog.UserLocales), () => s.Locale2,
                            v => { s.Locale2 = v; LocaleChanged(v, k => s.Keyboard2 = k, keyboard2, first: false); }),
                        keyboard2
                    ]),
                    Toggle("ua.lang.third", () => s.UseLocale3, v => s.UseLocale3 = v, children:
                    [
                        Sel("ua.lang.locale", Cat(UnattendCatalog.UserLocales), () => s.Locale3,
                            v => { s.Locale3 = v; LocaleChanged(v, k => s.Keyboard3 = k, keyboard3, first: false); }),
                        keyboard3
                    ]),
                    geo
                ]),
                Opt("i", "ua.lang.interactive"))
        ]);

        // ---- Этап Windows PE ----
        var edition = Group("ua.g.edition",
        [
            Choice(() => s.EditionMode, v => s.EditionMode = v,
                Opt(EditionMode.Interactive, "ua.edition.interactive"),
                Opt(EditionMode.Generic, "ua.edition.generic", "ua.edition.generic.desc",
                    [Sel("ua.edition.select", Cat(UnattendCatalog.Editions), () => s.GenericEdition, v => s.GenericEdition = v)]),
                Opt(EditionMode.Custom, "ua.edition.custom", null,
                    [Text("ua.edition.key", () => s.CustomProductKey, v => s.CustomProductKey = v)]),
                Opt(EditionMode.Firmware, "ua.edition.firmware", "ua.edition.firmware.desc"))
        ]);

        var targetDisk = Group("ua.g.target",
        [
            Choice(() => s.TargetDiskMode, v => s.TargetDiskMode = v,
                Opt(TargetDiskMode.Criteria, "ua.target.criteria", "ua.target.criteria.desc",
                [
                    Toggle("ua.target.noPartitions", () => s.TargetNoPartitions, v => s.TargetNoPartitions = v),
                    Toggle("ua.target.interface", () => s.TargetInterfaceType, v => s.TargetInterfaceType = v),
                    Toggle("ua.target.media", () => s.TargetMediaType, v => s.TargetMediaType = v),
                    Toggle("ua.target.capacity", () => s.TargetCapacity, v => s.TargetCapacity = v, children:
                    [
                        Num("ua.minGiB", () => s.TargetMinGiB, v => s.TargetMinGiB = v),
                        Num("ua.maxGiB", () => s.TargetMaxGiB, v => s.TargetMaxGiB = v)
                    ]),
                    Toggle("ua.target.index", () => s.TargetIndexEnabled, v => s.TargetIndexEnabled = v, "ua.target.index.desc",
                        [Num("ua.target.indexValue", () => s.TargetIndex, v => s.TargetIndex = v)])
                ]),
                Opt(TargetDiskMode.Script, "ua.target.script", "ua.target.script.desc",
                    [Area("ua.target.scriptValue", () => s.TargetDiskScript, v => s.TargetDiskScript = v)]),
                Opt(TargetDiskMode.Interactive, "ua.target.interactive"))
        ]);

        var partitioning = Group("ua.g.partition",
        [
            Choice(() => s.PartitionMode, v => s.PartitionMode = v,
                Opt(PartitionMode.Unattended, "ua.part.unattended", null,
                [
                    targetDisk,
                    Group("ua.g.layout",
                    [
                        Choice(() => s.PartitionLayout, v => s.PartitionLayout = v,
                            Opt(PartitionLayout.Automatic, "ua.layout.auto", "ua.layout.auto.desc"),
                            Opt(PartitionLayout.GPT, "ua.layout.gpt", "ua.layout.gpt.desc"),
                            Opt(PartitionLayout.MBR, "ua.layout.mbr", "ua.layout.mbr.desc"))
                    ]),
                    Group("ua.g.system",
                        [Num("ua.system.size", () => s.SystemPartitionSize, v => s.SystemPartitionSize = v)],
                        "ua.system.desc"),
                    Group("ua.g.re",
                    [
                        Choice(() => s.RecoveryMode, v => s.RecoveryMode = v,
                            Opt(RecoveryMode.Partition, "ua.re.partition", "ua.re.partition.desc",
                                [Num("ua.re.size", () => s.RecoveryPartitionSize, v => s.RecoveryPartitionSize = v)]),
                            Opt(RecoveryMode.None, "ua.re.none", "ua.re.none.desc"))
                    ])
                ]),
                Opt(PartitionMode.Custom, "ua.part.custom", "ua.part.custom.desc",
                    [Area("ua.part.script", () => s.DiskpartScript, v => s.DiskpartScript = v)]),
                Opt(PartitionMode.Interactive, "ua.part.interactive", "ua.part.interactive.desc"))
        ]);

        var generate = new FormItem[]
        {
            Toggle("ua.pe.dot3", () => s.Disable8Dot3Names, v => s.Disable8Dot3Names = v, "ua.pe.dot3.desc"),
            Toggle("ua.pe.defender", () => s.DisableDefender, v => s.DisableDefender = v, "ua.pe.defender.desc"),
            Toggle("ua.pe.pauseFormat", () => s.PauseBeforeFormatting, v => s.PauseBeforeFormatting = v),
            Toggle("ua.pe.pauseReboot", () => s.PauseBeforeReboot, v => s.PauseBeforeReboot = v),
            Toggle("ua.pe.compact", () => s.CompactOs, v => s.CompactOs = v),
            Toggle("ua.pe.skipIntegrity", () => s.SkipIntegrityCheck, v => s.SkipIntegrityCheck = v, "ua.pe.skipIntegrity.desc"),
            Group("ua.g.image",
            [
                Choice(() => s.ImageMode, v => s.ImageMode = v,
                    Opt(ImageMode.Edition, "ua.image.edition", null,
                        [Sel("ua.edition.select", Cat(UnattendCatalog.Editions), () => s.ImageEdition, v => s.ImageEdition = v)]),
                    Opt(ImageMode.Name, "ua.image.name", "ua.image.name.desc",
                        [Text("ua.image.nameValue", () => s.ImageName, v => s.ImageName = v)]),
                    Opt(ImageMode.Index, "ua.image.index", null,
                        [Num("ua.image.indexValue", () => s.ImageIndex, v => s.ImageIndex = v)]),
                    Opt(ImageMode.Interactive, "ua.image.interactive", "ua.image.interactive.desc"))
            ]),
            partitioning,
            Group("ua.g.assert",
            [
                Choice(() => s.AssertMode, v => s.AssertMode = v,
                    Opt(AssertMode.Skip, "ua.assert.skip"),
                    Opt(AssertMode.Generated, "ua.assert.generated", null,
                    [
                        Toggle("ua.assert.capacity", () => s.AssertCapacity, v => s.AssertCapacity = v, children:
                        [
                            Num("ua.minGiB", () => s.AssertMinGiB, v => s.AssertMinGiB = v),
                            Num("ua.maxGiB", () => s.AssertMaxGiB, v => s.AssertMaxGiB = v)
                        ]),
                        Toggle("ua.assert.noPartitions", () => s.AssertNoPartitions, v => s.AssertNoPartitions = v),
                        Toggle("ua.assert.interface", () => s.AssertInterfaceType, v => s.AssertInterfaceType = v),
                        Toggle("ua.assert.media", () => s.AssertMediaType, v => s.AssertMediaType = v)
                    ]),
                    Opt(AssertMode.Script, "ua.assert.script", null,
                        [Area("ua.assert.scriptValue", () => s.AssertScript, v => s.AssertScript = v)]))
            ]),
            Group("ua.g.paging",
            [
                Choice(() => s.PagingMode, v => s.PagingMode = v,
                    Opt(PagingMode.Automatic, "ua.paging.auto"),
                    Opt(PagingMode.Custom, "ua.paging.custom", null,
                    [
                        Num("ua.paging.initial", () => s.PagingInitialMiB, v => s.PagingInitialMiB = v),
                        Num("ua.paging.max", () => s.PagingMaxMiB, v => s.PagingMaxMiB = v)
                    ]),
                    Opt(PagingMode.None, "ua.paging.none"))
            ])
        };

        var pe = Sec("ua.s.pe",
        [
            Choice(() => s.PeMode, v => s.PeMode = v,
                Opt(PeMode.Interactive, "ua.pe.interactive", "ua.pe.interactive.desc", [edition]),
                Opt(PeMode.Generate, "ua.pe.generate", null, generate),
                Opt(PeMode.Script, "ua.pe.script", "ua.pe.script.desc",
                    [Area("ua.pe.scriptValue", () => s.PeScript, v => s.PeScript = v)]))
        ]);

        var activation = Sec("ua.s.activation",
            [Text("ua.activation.key", () => s.ActivationKey, v => s.ActivationKey = v, "ua.activation.key.desc")]);

        var arch = Sec("ua.s.arch",
        [
            Toggle("ua.arch.x86", () => s.ArchX86, v => s.ArchX86 = v),
            Toggle("ua.arch.amd64", () => s.ArchAmd64, v => s.ArchAmd64 = v),
            Toggle("ua.arch.arm64", () => s.ArchArm64, v => s.ArchArm64 = v)
        ], "ua.s.arch.desc");

        var setup = Sec("ua.s.setup",
        [
            Toggle("ua.setup.bypass", () => s.BypassRequirements, v => s.BypassRequirements = v, "ua.setup.bypass.desc"),
            Toggle("ua.setup.network", () => s.BypassNetwork, v => s.BypassNetwork = v, "ua.setup.network.desc"),
            Toggle("ua.setup.configSet", () => s.UseConfigurationSet, v => s.UseConfigurationSet = v, "ua.setup.configSet.desc"),
            Toggle("ua.setup.hidePs", () => s.HidePowerShellWindows, v => s.HidePowerShellWindows = v, "ua.setup.hidePs.desc"),
            Toggle("ua.setup.keepFiles", () => s.KeepSensitiveFiles, v => s.KeepSensitiveFiles = v, "ua.setup.keepFiles.desc"),
            Toggle("ua.setup.narrator", () => s.UseNarrator, v => s.UseNarrator = v, "ua.setup.narrator.desc")
        ]);

        var computer = Sec("ua.s.computer",
        [
            Choice(() => s.ComputerNameMode, v => s.ComputerNameMode = v,
                Opt(ComputerNameMode.Random, "ua.computer.random"),
                Opt(ComputerNameMode.Custom, "ua.computer.custom", null,
                    [Text("ua.computer.name", () => s.ComputerName, v => s.ComputerName = v)]),
                Opt(ComputerNameMode.Script, "ua.computer.script", "ua.computer.script.desc",
                    [Area("ua.computer.scriptValue", () => s.ComputerNameScript, v => s.ComputerNameScript = v)]))
        ]);

        var timeZone = Sec("ua.s.timezone",
        [
            Choice(() => s.TimeZoneExplicit ? "e" : "i", v => s.TimeZoneExplicit = v == "e",
                Opt("i", "ua.tz.implicit"),
                Opt("e", "ua.tz.explicit", "ua.tz.explicit.desc",
                    [Sel("ua.tz.select", Cat(UnattendCatalog.TimeZones), () => s.TimeZone, v => s.TimeZone = v)]))
        ]);

        // ---- Учётные записи ----
        var groups = new[]
        {
            new SelectOption(Constants.UsersGroup, () => _loc["ua.acc.groupUsers"]),
            new SelectOption(Constants.AdministratorsGroup, () => _loc["ua.acc.groupAdmins"])
        };
        var accounts = Sec("ua.s.accounts",
        [
            Choice(() => s.AccountMode, v => s.AccountMode = v,
                Opt(AccountMode.Unattended, "ua.acc.unattended", null,
                [
                    new FormAccounts(_ctx, s.Accounts.Select(r => new FormAccountRow(_ctx, r, groups)).ToList(), "ua.acc.display.desc"),
                    Group("ua.g.firstLogon",
                    [
                        Choice(() => s.AutoLogon, v => s.AutoLogon = v,
                            Opt(AutoLogonMode.Own, "ua.logon.own"),
                            Opt(AutoLogonMode.Builtin, "ua.logon.builtin", null,
                                [Text("ua.logon.adminPassword", () => s.AdministratorPassword, v => s.AdministratorPassword = v)]),
                            Opt(AutoLogonMode.None, "ua.logon.none", "ua.logon.none.desc")),
                        Toggle("ua.acc.obscure", () => s.ObscurePasswords, v => s.ObscurePasswords = v)
                    ], "ua.g.firstLogon.desc")
                ]),
                Opt(AccountMode.InteractiveMicrosoft, "ua.acc.microsoft"),
                Opt(AccountMode.InteractiveLocal, "ua.acc.local"))
        ]);

        var passwordExpiration = Sec("ua.s.pwexp",
        [
            Choice(() => s.PasswordExpiration, v => s.PasswordExpiration = v,
                Opt(PasswordExpirationMode.Unlimited, "ua.pw.unlimited", "ua.pw.unlimited.desc"),
                Opt(PasswordExpirationMode.Default, "ua.pw.default", "ua.pw.default.desc"),
                Opt(PasswordExpirationMode.Custom, "ua.pw.custom", "ua.pw.custom.desc",
                    [Num("ua.pw.days", () => s.PasswordMaxAge, v => s.PasswordMaxAge = v)]))
        ]);

        var lockout = Sec("ua.s.lockout",
        [
            Choice(() => s.LockoutMode, v => s.LockoutMode = v,
                Opt(LockoutMode.Default, "ua.lock.default", "ua.lock.default.desc"),
                Opt(LockoutMode.Disable, "ua.lock.disable", "ua.lock.disable.desc"),
                Opt(LockoutMode.Custom, "ua.lock.custom", null,
                [
                    Num("ua.lock.threshold", () => s.LockoutThreshold, v => s.LockoutThreshold = v),
                    Num("ua.lock.window", () => s.LockoutWindow, v => s.LockoutWindow = v),
                    Num("ua.lock.duration", () => s.LockoutDuration, v => s.LockoutDuration = v)
                ]))
        ]);

        // ---- Проводник, Пуск и панель задач ----
        var explorer = Sec("ua.s.explorer",
        [
            Choice(() => s.HideFiles, v => s.HideFiles = v,
                Opt(HideModes.Hidden, "ua.files.hidden", "ua.files.hidden.desc"),
                Opt(HideModes.HiddenSystem, "ua.files.hiddenSystem", "ua.files.hiddenSystem.desc"),
                Opt(HideModes.None, "ua.files.none", "ua.files.none.desc")),
            Toggle("ua.explorer.ext", () => s.ShowFileExtensions, v => s.ShowFileExtensions = v, "ua.explorer.ext.desc"),
            Toggle("ua.explorer.classic", () => s.ClassicContextMenu, v => s.ClassicContextMenu = v),
            Toggle("ua.explorer.infotip", () => s.HideInfoTip, v => s.HideInfoTip = v),
            Toggle("ua.explorer.thisPc", () => s.LaunchToThisPC, v => s.LaunchToThisPC = v),
            Toggle("ua.explorer.endTask", () => s.ShowEndTask, v => s.ShowEndTask = v)
        ]);

        var taskbar = Sec("ua.s.taskbar",
        [
            Choice(() => s.TaskbarSearch, v => s.TaskbarSearch = v,
            [
                Opt(TaskbarSearchMode.Box, "ua.search.box"),
                Opt(TaskbarSearchMode.Label, "ua.search.label"),
                Opt(TaskbarSearchMode.Icon, "ua.search.icon"),
                Opt(TaskbarSearchMode.Hide, "ua.search.hide")
            ], "ua.search.title"),
            Choice(() => s.TaskbarIcons, v => s.TaskbarIcons = v,
            [
                Opt(LayoutMode.Default, "ua.tbicons.default"),
                Opt(LayoutMode.Empty, "ua.tbicons.empty"),
                Opt(LayoutMode.Custom, "ua.tbicons.custom", "ua.tbicons.custom.desc",
                    [Area("ua.tbicons.xml", () => s.TaskbarIconsXml, v => s.TaskbarIconsXml = v)])
            ], "ua.tbicons.title"),
            Toggle("ua.tb.widgets", () => s.DisableWidgets, v => s.DisableWidgets = v, "ua.tb.widgets.desc"),
            Toggle("ua.tb.left", () => s.LeftTaskbar, v => s.LeftTaskbar = v),
            Toggle("ua.tb.taskview", () => s.HideTaskViewButton, v => s.HideTaskViewButton = v),
            Toggle("ua.tb.tray", () => s.ShowAllTrayIcons, v => s.ShowAllTrayIcons = v, "ua.tb.tray.desc"),
            Toggle("ua.tb.bing", () => s.DisableBingResults, v => s.DisableBingResults = v),
            Group("ua.g.win10",
            [
                Choice(() => s.StartTiles, v => s.StartTiles = v,
                    Opt(LayoutMode.Default, "ua.tiles.default", "ua.tiles.default.desc"),
                    Opt(LayoutMode.Empty, "ua.tiles.empty"),
                    Opt(LayoutMode.Custom, "ua.tiles.custom", "ua.tiles.custom.desc",
                        [Area("ua.tiles.xml", () => s.StartTilesXml, v => s.StartTilesXml = v)]))
            ]),
            Group("ua.g.win11",
            [
                Choice(() => s.StartPins, v => s.StartPins = v,
                    Opt(LayoutMode.Default, "ua.pins.default", "ua.pins.default.desc"),
                    Opt(LayoutMode.Empty, "ua.pins.empty"),
                    Opt(LayoutMode.Custom, "ua.pins.custom", "ua.pins.custom.desc",
                        [Area("ua.pins.json", () => s.StartPinsJson, v => s.StartPinsJson = v)]))
            ])
        ]);

        // ---- Системные настройки ----
        var system = Sec("ua.s.system",
        [
            Toggle("ua.sys.update", () => s.DisableWindowsUpdate, v => s.DisableWindowsUpdate = v, "ua.sys.update.desc"),
            Toggle("ua.sys.uac", () => s.DisableUac, v => s.DisableUac = v, "ua.sys.uac.desc"),
            Toggle("ua.sys.sac", () => s.DisableSac, v => s.DisableSac = v, "ua.sys.sac.desc"),
            Toggle("ua.sys.smartscreen", () => s.DisableSmartScreen, v => s.DisableSmartScreen = v),
            Toggle("ua.sys.fastStartup", () => s.DisableFastStartup, v => s.DisableFastStartup = v),
            Toggle("ua.sys.restore", () => s.DisableSystemRestore, v => s.DisableSystemRestore = v, "ua.sys.restore.desc"),
            Toggle("ua.sys.longPaths", () => s.EnableLongPaths, v => s.EnableLongPaths = v, "ua.sys.longPaths.desc"),
            Toggle("ua.sys.rdp", () => s.EnableRemoteDesktop, v => s.EnableRemoteDesktop = v),
            Toggle("ua.sys.acl", () => s.HardenSystemDriveAcl, v => s.HardenSystemDriveAcl = v, "ua.sys.acl.desc"),
            Toggle("ua.sys.junctions", () => s.DeleteJunctions, v => s.DeleteJunctions = v, "ua.sys.junctions.desc"),
            Toggle("ua.sys.ps", () => s.AllowPowerShellScripts, v => s.AllowPowerShellScripts = v, "ua.sys.ps.desc"),
            Toggle("ua.sys.lastAccess", () => s.DisableLastAccess, v => s.DisableLastAccess = v, "ua.sys.lastAccess.desc"),
            Toggle("ua.sys.noReboot", () => s.PreventAutomaticReboot, v => s.PreventAutomaticReboot = v, "ua.sys.noReboot.desc"),
            Toggle("ua.sys.sounds", () => s.TurnOffSystemSounds, v => s.TurnOffSystemSounds = v, "ua.sys.sounds.desc"),
            Toggle("ua.sys.suggestions", () => s.DisableAppSuggestions, v => s.DisableAppSuggestions = v, "ua.sys.suggestions.desc"),
            Toggle("ua.sys.encryption", () => s.PreventDeviceEncryption, v => s.PreventDeviceEncryption = v, "ua.sys.encryption.desc"),
            Toggle("ua.sys.edgeFre", () => s.HideEdgeFre, v => s.HideEdgeFre = v, "ua.sys.edgeFre.desc"),
            Toggle("ua.sys.edgeBoost", () => s.DisableEdgeStartupBoost, v => s.DisableEdgeStartupBoost = v, "ua.sys.edgeBoost.desc"),
            Toggle("ua.sys.edgeUninstall", () => s.MakeEdgeUninstallable, v => s.MakeEdgeUninstallable = v, "ua.sys.edgeUninstall.desc"),
            Toggle("ua.sys.pointer", () => s.DisablePointerPrecision, v => s.DisablePointerPrecision = v, "ua.sys.pointer.desc"),
            Toggle("ua.sys.windowsOld", () => s.DeleteWindowsOld, v => s.DeleteWindowsOld = v),
            Toggle("ua.sys.restartSignOn", () => s.DisableAutomaticRestartSignOn, v => s.DisableAutomaticRestartSignOn = v, "ua.sys.restartSignOn.desc"),
            Toggle("ua.sys.wpbt", () => s.DisableWpbt, v => s.DisableWpbt = v),
            Toggle("ua.sys.deviceApps", () => s.PreventDeviceApps, v => s.PreventDeviceApps = v, "ua.sys.deviceApps.desc"),
            Toggle("ua.sys.audit", () => s.ProcessAudit, v => s.ProcessAudit = v, "ua.sys.audit.desc",
                [Toggle("ua.sys.auditCmd", () => s.ProcessAuditCommandLine, v => s.ProcessAuditCommandLine = v)])
        ]);

        // ---- Визуальные эффекты, значки, папки ----
        var effects = Sec("ua.s.effects",
        [
            Choice(() => s.EffectsMode, v => s.EffectsMode = v,
                Opt(EffectsMode.Default, "ua.effects.default"),
                Opt(EffectsMode.BestAppearance, "ua.effects.appearance"),
                Opt(EffectsMode.BestPerformance, "ua.effects.performance"),
                Opt(EffectsMode.Custom, "ua.effects.custom", null,
                    [List(Enum.GetNames<Effect>().Select(e => Toggle($"ua.fx.{e}",
                        () => s.Effects.GetValueOrDefault(e), v => s.Effects[e] = v)))]))
        ]);

        var icons = Sec("ua.s.icons",
        [
            Toggle("ua.icons.edge", () => s.DeleteEdgeDesktopIcon, v => s.DeleteEdgeDesktopIcon = v),
            Choice(() => s.DesktopIconsCustom ? "c" : "d", v => s.DesktopIconsCustom = v == "c",
                Opt("d", "ua.icons.default"),
                Opt("c", "ua.icons.custom", null,
                    [List(UnattendCatalog.DesktopIcons.Select(i => Toggle($"ua.icon.{i.Id}",
                        () => s.DesktopIcons.GetValueOrDefault(i.Id), v => s.DesktopIcons[i.Id] = v)))]))
        ]);

        var folders = Sec("ua.s.folders",
        [
            Choice(() => s.StartFoldersCustom ? "c" : "d", v => s.StartFoldersCustom = v == "c",
                Opt("d", "ua.folders.default"),
                Opt("c", "ua.folders.custom", "ua.folders.custom.desc",
                    [List(UnattendCatalog.StartFolders.Select(f => Toggle($"ua.folder.{f.Id}",
                        () => s.StartFolders.GetValueOrDefault(f.Id), v => s.StartFolders[f.Id] = v)))]))
        ]);

        // ---- Виртуализация, Wi‑Fi, экспресс-параметры ----
        var vmHost = Sec("ua.s.vmhost",
        [
            Choice(() => s.DisableCoreIsolation ? "d" : "k", v => s.DisableCoreIsolation = v == "d",
                Opt("k", "ua.core.keep", "ua.core.keep.desc"),
                Opt("d", "ua.core.disable", "ua.core.disable.desc"))
        ]);

        var vmGuest = Sec("ua.s.vmguest",
        [
            Toggle("ua.vm.vbox", () => s.VBoxGuestAdditions, v => s.VBoxGuestAdditions = v),
            Toggle("ua.vm.vmware", () => s.VMwareTools, v => s.VMwareTools = v),
            Toggle("ua.vm.virtio", () => s.VirtIoGuestTools, v => s.VirtIoGuestTools = v),
            Toggle("ua.vm.parallels", () => s.ParallelsTools, v => s.ParallelsTools = v)
        ], "ua.s.vmguest.desc");

        var wifi = Sec("ua.s.wifi",
        [
            Choice(() => s.WifiMode, v => s.WifiMode = v,
                Opt(WifiMode.Interactive, "ua.wifi.interactive"),
                Opt(WifiMode.Skip, "ua.wifi.skip", "ua.wifi.skip.desc"),
                Opt(WifiMode.Parameters, "ua.wifi.params", null,
                [
                    Text("ua.wifi.ssid", () => s.WifiSsid, v => s.WifiSsid = v),
                    Toggle("ua.wifi.hidden", () => s.WifiNonBroadcast, v => s.WifiNonBroadcast = v),
                    Sel("ua.wifi.auth",
                    [
                        Loc(WifiAuthentications.Open, "ua.wifi.open"),
                        Loc(WifiAuthentications.WPA2PSK, "ua.wifi.wpa2"),
                        Loc(WifiAuthentications.WPA3SAE, "ua.wifi.wpa3")
                    ], () => s.WifiAuthentication.ToString(), v => s.WifiAuthentication = Enum.Parse<WifiAuthentications>(v)),
                    Text("ua.wifi.password", () => s.WifiPassword, v => s.WifiPassword = v),
                    Note("ua.wifi.driver.desc")
                ]),
                Opt(WifiMode.Xml, "ua.wifi.xml", "ua.wifi.xml.desc",
                    [Area("ua.wifi.xmlValue", () => s.WifiXml, v => s.WifiXml = v)]))
        ]);

        var express = Sec("ua.s.express",
        [
            Choice(() => s.ExpressSettings, v => s.ExpressSettings = v,
                Opt(ExpressSettingsMode.DisableAll, "ua.express.disable", "ua.express.disable.desc"),
                Opt(ExpressSettingsMode.EnableAll, "ua.express.enable", "ua.express.enable.desc"),
                Opt(ExpressSettingsMode.Interactive, "ua.express.interactive", "ua.express.interactive.desc"))
        ]);

        // ---- Клавиатура ----
        SelectOption[] initialStates = [Loc(LockKeyInitial.Off, "ua.lk.off"), Loc(LockKeyInitial.On, "ua.lk.on")];
        SelectOption[] behaviors = [Loc(LockKeyBehavior.Toggle, "ua.lk.toggle"), Loc(LockKeyBehavior.Ignore, "ua.lk.ignore")];

        var lockKeys = Sec("ua.s.lockkeys",
        [
            Choice(() => s.LockKeysCustom ? "c" : "d", v => s.LockKeysCustom = v == "c",
                Opt("d", "ua.lk.default"),
                Opt("c", "ua.lk.custom", "ua.lk.custom.desc",
                [
                    Sel("ua.lk.caps.initial", initialStates, () => s.CapsLockInitial.ToString(), v => s.CapsLockInitial = Enum.Parse<LockKeyInitial>(v)),
                    Sel("ua.lk.caps.behavior", behaviors, () => s.CapsLockBehavior.ToString(), v => s.CapsLockBehavior = Enum.Parse<LockKeyBehavior>(v)),
                    Sel("ua.lk.num.initial", initialStates, () => s.NumLockInitial.ToString(), v => s.NumLockInitial = Enum.Parse<LockKeyInitial>(v)),
                    Sel("ua.lk.num.behavior", behaviors, () => s.NumLockBehavior.ToString(), v => s.NumLockBehavior = Enum.Parse<LockKeyBehavior>(v)),
                    Sel("ua.lk.scroll.initial", initialStates, () => s.ScrollLockInitial.ToString(), v => s.ScrollLockInitial = Enum.Parse<LockKeyInitial>(v)),
                    Sel("ua.lk.scroll.behavior", behaviors, () => s.ScrollLockBehavior.ToString(), v => s.ScrollLockBehavior = Enum.Parse<LockKeyBehavior>(v))
                ]))
        ]);

        StickyKeys[] stickyOrder =
        [
            StickyKeys.HotKeyActive, StickyKeys.HotKeySound, StickyKeys.Indicator,
            StickyKeys.AudibleFeedback, StickyKeys.TriState, StickyKeys.TwoKeysOff
        ];
        var sticky = Sec("ua.s.sticky",
        [
            Choice(() => s.StickyMode, v => s.StickyMode = v,
                Opt(StickyMode.Default, "ua.sticky.default"),
                Opt(StickyMode.Disabled, "ua.sticky.disabled"),
                Opt(StickyMode.Custom, "ua.sticky.custom", "ua.sticky.custom.desc",
                    stickyOrder.Select(f => Toggle($"ua.sticky.{f}",
                        () => s.StickyFlags.GetValueOrDefault(f.ToString()), v => s.StickyFlags[f.ToString()] = v)).ToArray()))
        ]);

        // ---- Оформление ----
        SelectOption[] themes = [Loc(ColorTheme.Dark, "ua.theme.dark"), Loc(ColorTheme.Light, "ua.theme.light")];
        var colors = Sec("ua.s.colors",
        [
            Choice(() => s.ColorsCustom ? "c" : "d", v => s.ColorsCustom = v == "c",
                Opt("d", "ua.colors.default"),
                Opt("c", "ua.colors.custom", null,
                [
                    Sel("ua.colors.system", themes, () => s.SystemTheme.ToString(), v => s.SystemTheme = Enum.Parse<ColorTheme>(v)),
                    Sel("ua.colors.apps", themes, () => s.AppsTheme.ToString(), v => s.AppsTheme = Enum.Parse<ColorTheme>(v)),
                    Text("ua.colors.accent", () => s.AccentColor, v => s.AccentColor = v),
                    Toggle("ua.colors.onStart", () => s.AccentColorOnStart, v => s.AccentColorOnStart = v),
                    Toggle("ua.colors.onBorders", () => s.AccentColorOnBorders, v => s.AccentColorOnBorders = v),
                    Toggle("ua.colors.transparency", () => s.EnableTransparency, v => s.EnableTransparency = v)
                ]))
        ]);

        var wallpaper = Sec("ua.s.wallpaper",
        [
            Choice(() => s.WallpaperMode, v => s.WallpaperMode = v,
                Opt(WallpaperMode.Default, "ua.wp.default"),
                Opt(WallpaperMode.Solid, "ua.wp.solid", null,
                    [Text("ua.wp.color", () => s.WallpaperColor, v => s.WallpaperColor = v)]),
                Opt(WallpaperMode.Script, "ua.wp.script", "ua.wp.script.desc",
                    [Area("ua.wp.scriptValue", () => s.WallpaperScript, v => s.WallpaperScript = v)]))
        ]);

        var lockScreen = Sec("ua.s.lockscreen",
        [
            Choice(() => s.LockScreenScriptEnabled ? "s" : "d", v => s.LockScreenScriptEnabled = v == "s",
                Opt("d", "ua.ls.default"),
                Opt("s", "ua.ls.script", "ua.ls.script.desc",
                    [Area("ua.ls.scriptValue", () => s.LockScreenScript, v => s.LockScreenScript = v)]))
        ]);

        // ---- Удаление приложений ----
        var appToggles = UnattendCatalog.Bloatwares
            .Select(b => Toggle($"ua.app.{b.Id}", () => s.Bloatware.Contains(b.Id),
                v => { if (v) s.Bloatware.Add(b.Id); else s.Bloatware.Remove(b.Id); }))
            .ToList();
        var bloatware = Sec("ua.s.bloat",
        [
            Note("ua.s.bloat.desc"),
            new FormAppList(_ctx, appToggles, on =>
            {
                s.Bloatware.Clear();
                if (on) s.Bloatware.UnionWith(UnattendCatalog.Bloatwares.Select(b => b.Id));
            })
        ]);

        // ---- Установка программ после установки Windows ----
        _wingetToggles = WinStart.Core.Apps.WingetCatalog.Apps
            .Select(a => Toggle(a.Name, () => s.WingetApps.Contains(a.Id),
                v => { if (v) s.WingetApps.Add(a.Id); else s.WingetApps.Remove(a.Id); }))
            .ToList();
        var wingetGroups = Enum.GetValues<WinStart.Core.Apps.WingetCategory>()
            .Select(c => (Category: c, Items: WinStart.Core.Apps.WingetCatalog.Apps
                .Select((a, i) => (App: a, Toggle: _wingetToggles[i]))
                .Where(x => x.App.Category == c)
                .Select(x => x.Toggle)
                .ToList()))
            .Where(g => g.Items.Count > 0)
            .Select(g => (FormItem)Group($"programs.cat.{g.Category}", [List(g.Items)]))
            .ToList();
        var winget = Sec("ua.s.winget", [Note("ua.s.winget.desc"), .. wingetGroups]);

        // ---- Свои скрипты ----
        FormScripts Scripts(string titleKey, List<ScriptRow> rows, ScriptPhase phase)
        {
            var types = phase.GetAllowedTypes().Select(t => new SelectOption(t.ToString(), () => t.FileExtension())).ToList();
            return new FormScripts(_ctx, titleKey, titleKey + ".desc",
                rows.Select((r, i) => new FormScriptRow(_ctx, r, types, i + 1)).ToList());
        }

        var scripts = Sec("ua.s.scripts",
        [
            Scripts("ua.g.scriptsSystem", s.SystemScripts, ScriptPhase.System),
            Scripts("ua.g.scriptsDefaultUser", s.DefaultUserScripts, ScriptPhase.DefaultUser),
            Scripts("ua.g.scriptsFirstLogon", s.FirstLogonScripts, ScriptPhase.FirstLogon),
            Scripts("ua.g.scriptsUserOnce", s.UserOnceScripts, ScriptPhase.UserOnce),
            Toggle("ua.scripts.restartExplorer", () => s.RestartExplorer, v => s.RestartExplorer = v, "ua.scripts.restartExplorer.desc")
        ]);

        var appLocker = Sec("ua.s.applocker",
        [
            Choice(() => s.AppLockerEnabled ? "a" : "s", v => s.AppLockerEnabled = v == "a",
                Opt("s", "ua.applocker.skip"),
                Opt("a", "ua.applocker.apply", null,
                    [Area("ua.applocker.xml", () => s.AppLockerXml, v => s.AppLockerXml = v)]))
        ]);

        var componentOptions = UnattendCatalog.ComponentPasses.Select(cp =>
        {
            var key = UnattendCatalog.ComponentKey(cp.Component, cp.Pass);
            var label = $"{cp.Component.Id} – {cp.Pass}";
            return new SelectOption(key, () => s.Components.ContainsKey(key) ? "● " + label : label);
        }).ToList();
        var components = Sec("ua.s.components",
            [new FormComponents(_ctx, s.Components, componentOptions, "ua.components.select", "ua.s.components.desc")]);

        return
        [
            language, pe, activation, arch, setup, computer, timeZone, accounts, passwordExpiration, lockout,
            explorer, taskbar, system, effects, icons, folders, vmHost, vmGuest, wifi, express, lockKeys, sticky,
            colors, wallpaper, lockScreen, bloatware, winget, scripts, appLocker, components
        ];
    }

    // ---------------------------------------------------------------- фабрики элементов

    private FormSection Sec(string key, IReadOnlyList<FormItem> items, string? desc = null) => new(_ctx, key, items, desc);
    private FormGroup Group(string key, IReadOnlyList<FormItem> items, string? desc = null) => new(_ctx, key, items, desc);
    private FormNote Note(string key) => new(_ctx, key);

    private FormToggle Toggle(string key, Func<bool> get, Action<bool> set, string? desc = null,
        IReadOnlyList<FormItem>? children = null) => new(_ctx, key, get, set, desc, children);

    private FormToggleList List(IEnumerable<FormToggle> items) => new(_ctx, items.ToList());

    private FormOption Opt(string value, string key, string? desc = null, IReadOnlyList<FormItem>? children = null) =>
        new(_ctx, value, key, desc, children);

    private FormOption Opt<T>(T value, string key, string? desc = null, IReadOnlyList<FormItem>? children = null) where T : struct, Enum =>
        new(_ctx, value.ToString(), key, desc, children);

    private FormChoice Choice(Func<string> get, Action<string> set, params FormOption[] options) => new(_ctx, get, set, options);

    private FormChoice Choice(Func<string> get, Action<string> set, IReadOnlyList<FormOption> options, string titleKey) =>
        new(_ctx, get, set, options, titleKey);

    private FormChoice Choice<T>(Func<T> get, Action<T> set, params FormOption[] options) where T : struct, Enum =>
        new(_ctx, () => get().ToString(), v => set(Enum.Parse<T>(v)), options);

    private FormChoice Choice<T>(Func<T> get, Action<T> set, IReadOnlyList<FormOption> options, string titleKey) where T : struct, Enum =>
        new(_ctx, () => get().ToString(), v => set(Enum.Parse<T>(v)), options, titleKey);

    private FormSelect Sel(string key, IReadOnlyList<SelectOption> options, Func<string> get, Action<string> set, string? desc = null) =>
        new(_ctx, key, options, get, set, desc);

    private FormText Text(string key, Func<string> get, Action<string> set, string? desc = null) => new(_ctx, key, get, set, false, desc);
    private FormText Area(string key, Func<string> get, Action<string> set) => new(_ctx, key, get, set, multiline: true);
    private FormText Num(string key, Func<int> get, Action<int> set) => FormText.Number(_ctx, key, get, set);

    private static List<SelectOption> Cat(IReadOnlyList<UnattendChoice> items) =>
        items.Select(i => new SelectOption(i.Value, () => i.Title)).ToList();

    private SelectOption Loc<T>(T value, string key) where T : struct, Enum => new(value.ToString(), () => _loc[key]);
}
