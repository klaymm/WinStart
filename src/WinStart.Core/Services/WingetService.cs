using WinStart.Core.Abstractions;
using WinStart.Core.Apps;

namespace WinStart.Core.Services;

public sealed record WingetUpgrade(string Id, string Name, string Version, string Available);

public interface IWingetService
{
    Task<bool> IsAvailableAsync(CancellationToken ct);

    Task<HashSet<string>> ListInstalledAsync(CancellationToken ct);

    Task<IReadOnlyList<WingetUpgrade>> ListUpgradesAsync(CancellationToken ct);

    Task<string?> InstallAsync(string id, CancellationToken ct);

    Task<string?> UpgradeAsync(string id, CancellationToken ct);
}

public sealed class WingetService(IProcessRunner process) : IWingetService
{
    private const string Common = "--source winget --accept-source-agreements --disable-interactivity";

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
        var r = await process.RunAsync(Winget, $"list {Common}", ct, timeoutMs: 120000).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<WingetUpgrade>> ListUpgradesAsync(CancellationToken ct)
    {
        var r = await process.RunAsync(Winget, $"upgrade {Common}", ct, timeoutMs: 180000).ConfigureAwait(false);
        return r.ExitCode is 0 ? ParseUpgrades(ProcessRunner.OemToUtf8(r.StdOut)) : [];
    }

    public async Task<string?> InstallAsync(string id, CancellationToken ct)
    {
        var r = await process.RunAsync(Winget, WingetCatalog.InstallArguments(id), ct, timeoutMs: 1_800_000)
            .ConfigureAwait(false);
        return r.Ok ? null : Error(r);
    }

    public async Task<string?> UpgradeAsync(string id, CancellationToken ct)
    {
        var r = await process.RunAsync(Winget,
            $"upgrade --id {id} --exact --silent --accept-package-agreements {Common}", ct, timeoutMs: 1_800_000)
            .ConfigureAwait(false);
        return r.Ok ? null : Error(r);
    }

    public static List<WingetUpgrade> ParseUpgrades(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n')
            .Select(l => l.LastIndexOf('\r') is var i and >= 0 ? l[(i + 1)..] : l)
            .ToList();

        var dashes = lines.FindIndex(l => l.Trim().Length >= 10 && l.Trim().All(c => c == '-'));
        if (dashes < 1 || lines.Take(dashes - 1).Any(l => l.Any(char.IsLetter))) return [];

        var header = lines[dashes - 1];
        var starts = new List<int>();
        for (var i = 0; i < header.Length; i++)
            if (!char.IsWhiteSpace(header[i]) && (i == 0 || char.IsWhiteSpace(header[i - 1])))
                starts.Add(i);
        if (starts.Count < 4) return [];

        var result = new List<WingetUpgrade>();
        foreach (var line in lines.Skip(dashes + 1))
        {
            if (line.Length <= starts[3]) break;

            string Column(int index)
            {
                var from = starts[index];
                var to = index + 1 < starts.Count ? Math.Min(starts[index + 1], line.Length) : line.Length;
                return from < to ? line[from..to].Trim() : "";
            }

            var id = Column(1);
            if (id.Length == 0 || id.Contains(' ')) break;
            result.Add(new WingetUpgrade(id, Column(0), Column(2), Column(3)));
        }

        return result;
    }

    private static string Error(ProcessResult r)
    {
        var text = r.ExitCode is -1 or -2 ? r.StdErr : ProcessRunner.OemToUtf8(r.StdOut + "\n" + r.StdErr);
        var lines = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Any(char.IsLetter) && !l.Contains('█') && !l.Contains('▒'))
            .ToList();
        return lines.Count > 0 ? $"{lines[^1]} (exit code {r.ExitCode})" : $"exit code {r.ExitCode}";
    }
}
