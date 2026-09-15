using System.Text;
using System.Text.Json;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Unattend;

public sealed record WindowsImageInfo(int Index, string Name, string Edition, string Architecture, string Version,
    IReadOnlyList<string> Languages)
{
    public int Build => Version.Split('.') is { Length: >= 3 } p && int.TryParse(p[2], out var b) ? b : 0;

    public string Family => Build >= 22000 ? "Windows 11" : "Windows 10";
}

public sealed record RemovableDrive(string Root, string Label, long FreeBytes)
{
    public override string ToString() => string.IsNullOrEmpty(Label) ? Root : $"{Root}  {Label}";
}

public interface IImageCheckService
{
    Task<IReadOnlyList<WindowsImageInfo>> InspectAsync(string path, CancellationToken ct);

    IReadOnlyList<RemovableDrive> RemovableDrives();
}

public sealed class ImageCheckService(IProcessRunner process) : IImageCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<IReadOnlyList<WindowsImageInfo>> InspectAsync(string path, CancellationToken ct)
    {
        path = path.Trim().Trim('"');
        if (path.Length == 0) throw new ArgumentException("path");

        var literal = path.Replace("'", "''");
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $p = '{{literal}}'
            $mounted = $false
            $wim = $null
            try {
              if ($p -match '\.iso$') {
                $img = Mount-DiskImage -ImagePath $p -PassThru
                $mounted = $true
                Start-Sleep -Milliseconds 500
                $letter = ($img | Get-Volume).DriveLetter
                if (-not $letter) { $letter = (Get-DiskImage -ImagePath $p | Get-Volume).DriveLetter }
                if (-not $letter) { throw 'NOMOUNT' }
                $root = "$letter`:\"
              } elseif (Test-Path -LiteralPath $p -PathType Container) {
                $root = $p
              } else {
                $wim = $p
              }
              if (-not $wim) {
                $wim = Join-Path $root 'sources\install.wim'
                if (-not (Test-Path -LiteralPath $wim)) { $wim = Join-Path $root 'sources\install.esd' }
                if (-not (Test-Path -LiteralPath $wim)) { $wim = Join-Path $root 'sources\install.swm' }
              }
              if (-not (Test-Path -LiteralPath $wim)) { throw 'NOWIM' }
              $list = @(Get-WindowsImage -ImagePath $wim)
              $result = foreach ($i in $list) {
                $d = Get-WindowsImage -ImagePath $wim -Index $i.ImageIndex
                [pscustomobject]@{
                  Index = [int]$i.ImageIndex
                  Name = [string]$d.ImageName
                  Edition = [string]$d.EditionId
                  Architecture = [string]$d.Architecture
                  Version = [string]$d.Version
                  Languages = @($d.Languages | ForEach-Object { [string]$_ })
                }
              }
              $json = ConvertTo-Json -InputObject @($result) -Compress -Depth 4
              'B64:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
            } finally {
              if ($mounted) { Dismount-DiskImage -ImagePath $p | Out-Null }
            }
            """;

        var r = await process.PowerShellAsync(script, ct, 600000).ConfigureAwait(false);
        if (!r.Ok)
        {
            var error = (string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr).Trim();
            if (error.Contains("NOWIM")) throw new ImageCheckException(ImageCheckError.NoInstallImage);
            if (error.Contains("NOMOUNT")) throw new ImageCheckException(ImageCheckError.MountFailed);
            throw new ImageCheckException(ImageCheckError.Other, error.Split('\n')[0].Trim());
        }

        var marker = r.StdOut.IndexOf("B64:", StringComparison.Ordinal);
        if (marker < 0) throw new ImageCheckException(ImageCheckError.NoInstallImage);
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(r.StdOut[(marker + 4)..].Trim()));

        var rows = JsonSerializer.Deserialize<List<Row>>(json, JsonOptions) ?? [];
        return rows.Select(x => new WindowsImageInfo(x.Index, x.Name ?? "", x.Edition ?? "",
                NormalizeArchitecture(x.Architecture), x.Version ?? "", x.Languages ?? []))
            .ToList();
    }

    public IReadOnlyList<RemovableDrive> RemovableDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady)
            .Select(d => new RemovableDrive(d.RootDirectory.FullName, SafeLabel(d), d.AvailableFreeSpace))
            .ToList();

    private static string SafeLabel(DriveInfo d)
    {
        try { return d.VolumeLabel; } catch { return ""; }
    }

    private static string NormalizeArchitecture(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "9" or "x64" or "amd64" => "amd64",
        "0" or "x86" => "x86",
        "12" or "arm64" => "arm64",
        "5" or "arm" => "arm",
        null or "" => "",
        var other => other
    };

    private sealed class Row
    {
        public int Index { get; set; }
        public string? Name { get; set; }
        public string? Edition { get; set; }
        public string? Architecture { get; set; }
        public string? Version { get; set; }
        public List<string>? Languages { get; set; }
    }
}

public enum ImageCheckError { NoInstallImage, MountFailed, Other }

public sealed class ImageCheckException(ImageCheckError kind, string? detail = null) : Exception(detail ?? kind.ToString())
{
    public ImageCheckError Kind { get; } = kind;
}
