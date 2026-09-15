namespace WinStart.Installer;

public sealed class Localization
{
    private static readonly Dictionary<string, (string Ru, string En)> Strings = new()
    {
        ["title"] = ("Установка WinStart", "Install WinStart"),
        ["welcome"] = ("Добро пожаловать в WinStart", "Welcome to WinStart"),
        ["subtitle"] = ("Современная настройка Windows 11.\nНажмите «Установить», чтобы продолжить.",
                        "A modern way to tune Windows 11.\nClick “Install” to continue."),
        ["location"] = ("Папка установки", "Install location"),
        ["browse"] = ("Обзор…", "Browse…"),
        ["desktopShortcut"] = ("Создать ярлык на рабочем столе", "Create a desktop shortcut"),
        ["install"] = ("Установить", "Install"),
        ["cancel"] = ("Отмена", "Cancel"),
        ["installing"] = ("Устанавливаю…", "Installing…"),
        ["extracting"] = ("Распаковка файлов…", "Extracting files…"),
        ["shortcuts"] = ("Создание ярлыков…", "Creating shortcuts…"),
        ["registering"] = ("Регистрация…", "Registering…"),
        ["done"] = ("Установка завершена", "Installation complete"),
        ["doneText"] = ("WinStart установлен и готов к работе.", "WinStart is installed and ready to go."),
        ["run"] = ("Запустить WinStart", "Launch WinStart"),
        ["finish"] = ("Готово", "Finish"),
        ["failed"] = ("Не удалось установить", "Installation failed"),
        ["close"] = ("Закрыть", "Close"),

        ["alreadyInstalled"] = ("WinStart уже установлен", "WinStart is already installed"),
        ["alreadyInstalledText"] = (
            "Программа найдена на этом компьютере.\nМожно переустановить её или удалить.",
            "The app was found on this computer.\nYou can reinstall or remove it."),
        ["installedTo"] = ("Установлена в", "Installed in"),
        ["reinstall"] = ("Переустановить", "Reinstall"),
        ["uninstall"] = ("Удалить", "Remove"),
        ["removingOld"] = ("Удаление прошлой версии…", "Removing the previous version…"),
        ["removing"] = ("Удаление…", "Removing…"),
        ["updating"] = ("Обновление WinStart…", "Updating WinStart…"),
        ["removed"] = ("Программа удалена", "WinStart removed"),
        ["removedText"] = ("WinStart удалён с компьютера.", "WinStart has been removed from your computer.")
    };

    public string Lang { get; private set; } = "ru";
    public event EventHandler? Changed;

    public string this[string key] =>
        Strings.TryGetValue(key, out var v) ? (Lang == "en" ? v.En : v.Ru) : key;

    public void Toggle(string lang)
    {
        if (lang == Lang) return;
        Lang = lang;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
