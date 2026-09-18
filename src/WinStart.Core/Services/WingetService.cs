using WinStart.Core.Abstractions;
using WinStart.Core.Apps;

namespace WinStart.Core.Services;

public interface IWingetService
{
    Task<bool> IsAvailableAsync(CancellationToken ct);

    Task<HashSet<string>> ListInstalledAsync(CancellationToken ct);

    Task<string?> InstallAsync(string id, CancellationToken ct);
}

public sealed class WingetService(IProcessRunner process) : IWingetService
{
    private static string Winget
    {
        get
        {
            var alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\WindowsApps\winget.exe");
            return File.Exists(alias) ? alias : "winget.exe";
        }
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct)
    {
        var r = await process.RunAsync(Winget, "--version", ct, timeoutMs: 30000).ConfigureAwait(false);
        return r.Ok;
    }

    public async Task<HashSet<string>> ListInstalledAsync(CancellationToken ct)
    {
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var r = await process.RunAsync(Winget, "list --source winget --accept-source-agreements --disable-interactivity", ct,
            timeoutMs: 120000).ConfigureAwait(false);
        if (!r.Ok) return installed;

        foreach (var line in r.StdOut.Split('\n'))
        {
            foreach (var app in WingetCatalog.Apps)
            {
                if (installed.Contains(app.Id)) continue;
                var idx = line.IndexOf(app.Id, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;

                var after = idx + app.Id.Length;
                var before = idx == 0 || char.IsWhiteSpace(line[idx - 1]);
                var afterOk = after >= line.Length || char.IsWhiteSpace(line[after]);
                if (before && afterOk) installed.Add(app.Id);
            }
        }

        return installed;
    }

    public async Task<string?> InstallAsync(string id, CancellationToken ct)
    {
        var r = await process.RunAsync(Winget, WingetCatalog.InstallArguments(id), ct, timeoutMs: 1_800_000)
            .ConfigureAwait(false);
        if (r.Ok) return null;

        var lines = (r.StdOut + "\n" + r.StdErr).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Any(char.IsLetter) && !l.Contains('█') && !l.Contains('▒'))
            .ToList();
        return lines.Count > 0 ? $"{lines[^1]} (exit code {r.ExitCode})" : $"exit code {r.ExitCode}";
    }
}
