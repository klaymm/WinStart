using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public sealed class PathProvider : IPathProvider
{
    public PathProvider(string? appDirectory = null)
    {
        var baseDir = appDirectory ?? AppContext.BaseDirectory;
        Tools = Path.Combine(baseDir, "Tools");

        InstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinStart Tools");

        UserData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinStart");

        Backups = Path.Combine(UserData, "backups");
        Logs = Path.Combine(UserData, "logs");

        foreach (var dir in new[] { UserData, Backups, Logs })
            Directory.CreateDirectory(dir);
    }

    public string Tools { get; }
    public string InstallRoot { get; }
    public string UserData { get; }
    public string Backups { get; }
    public string Logs { get; }

    public string Tool(string fileName) => Path.Combine(Tools, fileName);

    public string Temp(string fileName) => Path.Combine(Path.GetTempPath(), "WinStart", fileName);
}
