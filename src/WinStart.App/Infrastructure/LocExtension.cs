using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using WinStart.Core.Abstractions;

namespace WinStart.App.Infrastructure;

[MarkupExtensionReturnType(typeof(object))]
public sealed class TextExtension : MarkupExtension
{
    public TextExtension() { }
    public TextExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationProxy.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}

public sealed class LocalizationProxy : System.ComponentModel.INotifyPropertyChanged
{
    public static LocalizationProxy Instance { get; } = new();

    private ILocalizationService? _service;

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public void Attach(ILocalizationService service)
    {
        _service = service;
        _service.LanguageChanged += (_, _) =>
            Application.Current?.Dispatcher.Invoke(() =>
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs("Item[]")));
    }

    public string this[string key] => _service?[key] ?? key;
}
