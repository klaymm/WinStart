using CommunityToolkit.Mvvm.ComponentModel;
using WinStart.Core.Abstractions;

namespace WinStart.App.ViewModels;

public sealed class FormContext(ILocalizationService loc, Action changed)
{
    public ILocalizationService Loc { get; } = loc;
    public Action Changed { get; } = changed;
}

public abstract class FormItem(FormContext ctx, string? titleKey, string? descriptionKey) : ObservableObject
{
    private bool _isVisible = true;

    protected FormContext Ctx { get; } = ctx;

    public string Title => titleKey is null ? "" : Ctx.Loc[titleKey];
    public string? Description => descriptionKey is null ? null : Ctx.Loc[descriptionKey];

    public virtual IEnumerable<FormItem> Children => [];

    public bool IsVisible
    {
        get => _isVisible;
        protected set => SetProperty(ref _isVisible, value);
    }

    public virtual bool ApplyFilter(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || Matches(query))
        {
            ShowAll();
            return true;
        }

        var any = false;
        foreach (var child in Children) any |= child.ApplyFilter(query);
        IsVisible = any;
        return any;
    }

    public void ShowAll()
    {
        IsVisible = true;
        foreach (var child in Children) child.ShowAll();
    }

    protected virtual bool Matches(string query) => Contains(Title, query) || Contains(Description, query);

    protected static bool Contains(string? text, string query) =>
        text is not null && text.Contains(query.Trim(), StringComparison.CurrentCultureIgnoreCase);

    public virtual void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        foreach (var child in Children) child.RefreshTexts();
    }
}

public sealed class FormSection(FormContext ctx, string titleKey, IReadOnlyList<FormItem> items, string? descriptionKey = null)
    : FormItem(ctx, titleKey, descriptionKey)
{
    private bool _isCurrent;

    public IReadOnlyList<FormItem> Items { get; } = items;
    public override IEnumerable<FormItem> Children => Items;

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }
}

public sealed class FormGroup(FormContext ctx, string titleKey, IReadOnlyList<FormItem> items, string? descriptionKey = null)
    : FormItem(ctx, titleKey, descriptionKey)
{
    public IReadOnlyList<FormItem> Items { get; } = items;
    public override IEnumerable<FormItem> Children => Items;
}

public sealed class FormNote(FormContext ctx, string textKey) : FormItem(ctx, null, textKey);

public sealed class FormToggle(FormContext ctx, string titleKey, Func<bool> get, Action<bool> set,
    string? descriptionKey = null, IReadOnlyList<FormItem>? children = null)
    : FormItem(ctx, titleKey, descriptionKey)
{
    public IReadOnlyList<FormItem> Items { get; } = children ?? [];
    public bool HasItems => Items.Count > 0;
    public override IEnumerable<FormItem> Children => Items;

    public bool IsOn
    {
        get => get();
        set
        {
            if (get() == value) return;
            set(value);
            OnPropertyChanged();
            Ctx.Changed();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(IsOn));
}

public sealed class FormToggleList(FormContext ctx, IReadOnlyList<FormToggle> items, string? titleKey = null)
    : FormItem(ctx, titleKey, null)
{
    public IReadOnlyList<FormToggle> Items { get; } = items;
    public override IEnumerable<FormItem> Children => Items;
}

public sealed class FormOption(FormContext ctx, string value, string titleKey, string? descriptionKey = null,
    IReadOnlyList<FormItem>? children = null)
    : FormItem(ctx, titleKey, descriptionKey)
{
    private bool _isSelected;

    public string Value { get; } = value;
    public IReadOnlyList<FormItem> Items { get; } = children ?? [];
    public bool HasItems => Items.Count > 0;
    public override IEnumerable<FormItem> Children => Items;

    internal FormChoice Owner { get; set; } = null!;

    public string GroupName => Owner.GroupName;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (value) Owner.Select(this);
            else SetProperty(ref _isSelected, false);
        }
    }

    internal void SetSelectedSilently(bool value) => SetProperty(ref _isSelected, value, nameof(IsSelected));
}

public sealed class FormChoice : FormItem
{
    private readonly Func<string> _get;
    private readonly Action<string> _set;

    public FormChoice(FormContext ctx, Func<string> get, Action<string> set, IReadOnlyList<FormOption> options,
        string? titleKey = null)
        : base(ctx, titleKey, null)
    {
        _get = get;
        _set = set;
        Options = options;
        GroupName = Guid.NewGuid().ToString("N");

        foreach (var o in options)
        {
            o.Owner = this;
            o.SetSelectedSilently(o.Value == get());
        }
    }

    public IReadOnlyList<FormOption> Options { get; }
    public string GroupName { get; }
    public override IEnumerable<FormItem> Children => Options;

    public override bool ApplyFilter(string? query)
    {
        var visible = base.ApplyFilter(query);
        if (visible) ShowAll();
        return visible;
    }

    internal void Select(FormOption option)
    {
        if (_get() == option.Value && option.IsSelected) return;

        _set(option.Value);
        foreach (var o in Options) o.SetSelectedSilently(o == option);
        Ctx.Changed();
    }
}

public sealed class SelectOption(string value, Func<string> title) : ObservableObject
{
    public string Value { get; } = value;
    public string Title => title();
    public void RefreshTexts() => OnPropertyChanged(nameof(Title));
    public override string ToString() => Title;
}

public sealed class FormSelect : FormItem
{
    private readonly Func<string> _get;
    private readonly Action<string> _set;

    public FormSelect(FormContext ctx, string? titleKey, IReadOnlyList<SelectOption> options,
        Func<string> get, Action<string> set, string? descriptionKey = null)
        : base(ctx, titleKey, descriptionKey)
    {
        Options = options;
        _get = get;
        _set = set;
    }

    public IReadOnlyList<SelectOption> Options { get; }

    public SelectOption? Selected
    {
        get => Options.FirstOrDefault(o => o.Value == _get()) ?? Options.FirstOrDefault();
        set
        {
            if (value is null || value.Value == _get()) return;
            _set(value.Value);
            OnPropertyChanged();
            Ctx.Changed();
        }
    }

    public void Refresh() => OnPropertyChanged(nameof(Selected));

    public override void RefreshTexts()
    {
        base.RefreshTexts();
        foreach (var o in Options) o.RefreshTexts();
        OnPropertyChanged(nameof(Selected));
    }
}

public sealed class FormText : FormItem
{
    private readonly Func<string> _get;
    private readonly Action<string> _set;
    private readonly string? _placeholderKey;

    public FormText(FormContext ctx, string? titleKey, Func<string> get, Action<string> set,
        bool multiline = false, string? descriptionKey = null, string? placeholderKey = null)
        : base(ctx, titleKey, descriptionKey)
    {
        _get = get;
        _set = set;
        _placeholderKey = placeholderKey;
        Multiline = multiline;
    }

    public static FormText Number(FormContext ctx, string titleKey, Func<int> get, Action<int> set) =>
        new(ctx, titleKey, () => get().ToString(), v => { if (int.TryParse(v.Trim(), out var n)) set(n); });

    public bool Multiline { get; }
    public string? Placeholder => _placeholderKey is null ? null : Ctx.Loc[_placeholderKey];

    protected override bool Matches(string query) => base.Matches(query) || Contains(Placeholder, query);

    public string Text
    {
        get => _get();
        set
        {
            if (_get() == value) return;
            _set(value);
            OnPropertyChanged();
            Ctx.Changed();
        }
    }

    public override void RefreshTexts()
    {
        base.RefreshTexts();
        OnPropertyChanged(nameof(Placeholder));
    }
}

public sealed class FormAccountRow(FormContext ctx, Core.Unattend.AccountRow row, IReadOnlyList<SelectOption> groups) : ObservableObject
{
    public string Name
    {
        get => row.Name;
        set { if (row.Name == value) return; row.Name = value; OnPropertyChanged(); ctx.Changed(); }
    }

    public string DisplayName
    {
        get => row.DisplayName;
        set { if (row.DisplayName == value) return; row.DisplayName = value; OnPropertyChanged(); ctx.Changed(); }
    }

    public string Password
    {
        get => row.Password;
        set { if (row.Password == value) return; row.Password = value; OnPropertyChanged(); ctx.Changed(); }
    }

    public IReadOnlyList<SelectOption> Groups { get; } = groups;

    public SelectOption? Group
    {
        get => Groups.FirstOrDefault(g => g.Value == row.Group);
        set
        {
            if (value is null || value.Value == row.Group) return;
            row.Group = value.Value;
            OnPropertyChanged();
            ctx.Changed();
        }
    }

    public void RefreshTexts()
    {
        foreach (var g in Groups) g.RefreshTexts();
        OnPropertyChanged(nameof(Group));
    }
}

public sealed class FormAccounts(FormContext ctx, IReadOnlyList<FormAccountRow> rows, string descriptionKey)
    : FormItem(ctx, null, descriptionKey)
{
    public IReadOnlyList<FormAccountRow> Rows { get; } = rows;

    public string NameHeader => Ctx.Loc["ua.acc.name"];
    public string DisplayNameHeader => Ctx.Loc["ua.acc.display"];
    public string PasswordHeader => Ctx.Loc["ua.acc.password"];
    public string GroupHeader => Ctx.Loc["ua.acc.group"];

    public override void RefreshTexts()
    {
        base.RefreshTexts();
        foreach (var n in new[] { nameof(NameHeader), nameof(DisplayNameHeader), nameof(PasswordHeader), nameof(GroupHeader) })
            OnPropertyChanged(n);
        foreach (var r in Rows) r.RefreshTexts();
    }
}

public sealed class FormScriptRow(FormContext ctx, Core.Unattend.ScriptRow row, IReadOnlyList<SelectOption> types, int number)
    : ObservableObject
{
    public string Title => ctx.Loc.Format("ua.scripts.row", number);
    public IReadOnlyList<SelectOption> Types { get; } = types;

    public SelectOption? Type
    {
        get => Types.FirstOrDefault(t => t.Value == row.Type.ToString());
        set
        {
            if (value is null || value.Value == row.Type.ToString()) return;
            row.Type = Enum.Parse<Schneegans.Unattend.ScriptType>(value.Value);
            OnPropertyChanged();
            ctx.Changed();
        }
    }

    public string Content
    {
        get => row.Content;
        set { if (row.Content == value) return; row.Content = value; OnPropertyChanged(); ctx.Changed(); }
    }

    public void RefreshTexts() => OnPropertyChanged(nameof(Title));
}

public sealed class FormScripts(FormContext ctx, string titleKey, string descriptionKey, IReadOnlyList<FormScriptRow> rows)
    : FormItem(ctx, titleKey, descriptionKey)
{
    public IReadOnlyList<FormScriptRow> Rows { get; } = rows;

    public override void RefreshTexts()
    {
        base.RefreshTexts();
        foreach (var r in Rows) r.RefreshTexts();
    }
}

public sealed class FormComponents : FormItem
{
    private readonly Dictionary<string, string> _values;
    private SelectOption _selected;

    public FormComponents(FormContext ctx, Dictionary<string, string> values, IReadOnlyList<SelectOption> options,
        string titleKey, string descriptionKey)
        : base(ctx, titleKey, descriptionKey)
    {
        _values = values;
        Options = options;
        _selected = options[0];
    }

    public IReadOnlyList<SelectOption> Options { get; }

    public SelectOption Selected
    {
        get => _selected;
        set
        {
            if (!SetProperty(ref _selected, value)) return;
            OnPropertyChanged(nameof(Xml));
        }
    }

    public string Xml
    {
        get => _values.GetValueOrDefault(_selected.Value) ?? "";
        set
        {
            if (Xml == value) return;
            if (string.IsNullOrWhiteSpace(value)) _values.Remove(_selected.Value);
            else _values[_selected.Value] = value;
            OnPropertyChanged();
            _selected.RefreshTexts();
            Ctx.Changed();
        }
    }

    public bool Has(string key) => _values.ContainsKey(key);
}

public sealed class FormAppList : FormItem
{
    private readonly Action<bool> _setAll;

    public FormAppList(FormContext ctx, IReadOnlyList<FormToggle> items, Action<bool> setAll) : base(ctx, null, null)
    {
        Items = items;
        _setAll = setAll;
        SelectAllCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SetAll(true));
        ClearAllCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() => SetAll(false));
    }

    public IReadOnlyList<FormToggle> Items { get; }
    public override IEnumerable<FormItem> Children => Items;

    public System.Windows.Input.ICommand SelectAllCommand { get; }
    public System.Windows.Input.ICommand ClearAllCommand { get; }

    private void SetAll(bool on)
    {
        _setAll(on);
        foreach (var t in Items) t.Refresh();
        Ctx.Changed();
    }
}
