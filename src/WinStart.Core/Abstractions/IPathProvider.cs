namespace WinStart.Core.Abstractions;

public interface IPathProvider
{
    string Tools { get; }

    string InstallRoot { get; }

    string UserData { get; }

    string Backups { get; }
    string Logs { get; }

    string Tool(string fileName);
    string Temp(string fileName);
}
