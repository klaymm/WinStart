namespace WinStart.Core.Apps;

public enum WingetCategory { Browsers, Messengers, Media, Archivers, Tools, Games, Office }

public sealed record WingetApp(string Id, string Name, WingetCategory Category);

public static class WingetCatalog
{
    public static IReadOnlyList<WingetApp> Apps { get; } =
    [
        new("Google.Chrome", "Google Chrome", WingetCategory.Browsers),
        new("Mozilla.Firefox", "Mozilla Firefox", WingetCategory.Browsers),
        new("Brave.Brave", "Brave", WingetCategory.Browsers),
        new("Yandex.Browser", "Яндекс Браузер", WingetCategory.Browsers),
        new("Opera.Opera", "Opera", WingetCategory.Browsers),

        new("Telegram.TelegramDesktop", "Telegram", WingetCategory.Messengers),
        new("Discord.Discord", "Discord", WingetCategory.Messengers),
        new("Zoom.Zoom", "Zoom", WingetCategory.Messengers),

        new("VideoLAN.VLC", "VLC media player", WingetCategory.Media),
        new("Spotify.Spotify", "Spotify", WingetCategory.Media),
        new("OBSProject.OBSStudio", "OBS Studio", WingetCategory.Media),
        new("GIMP.GIMP", "GIMP", WingetCategory.Media),
        new("dotPDNLLC.paintdotnet", "Paint.NET", WingetCategory.Media),

        new("7zip.7zip", "7-Zip", WingetCategory.Archivers),
        new("RARLab.WinRAR", "WinRAR", WingetCategory.Archivers),
        new("qBittorrent.qBittorrent", "qBittorrent", WingetCategory.Archivers),

        new("Notepad++.Notepad++", "Notepad++", WingetCategory.Tools),
        new("Microsoft.VisualStudioCode", "Visual Studio Code", WingetCategory.Tools),
        new("Microsoft.PowerToys", "PowerToys", WingetCategory.Tools),
        new("voidtools.Everything", "Everything", WingetCategory.Tools),
        new("Git.Git", "Git", WingetCategory.Tools),

        new("Valve.Steam", "Steam", WingetCategory.Games),
        new("EpicGames.EpicGamesLauncher", "Epic Games Launcher", WingetCategory.Games),

        new("TheDocumentFoundation.LibreOffice", "LibreOffice", WingetCategory.Office),
        new("ONLYOFFICE.DesktopEditors", "ONLYOFFICE", WingetCategory.Office),
        new("SumatraPDF.SumatraPDF", "Sumatra PDF", WingetCategory.Office),
        new("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", WingetCategory.Office)
    ];

    public static WingetApp? ById(string id) => Apps.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string InstallArguments(string id) =>
        $"install --id {id} --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
}
