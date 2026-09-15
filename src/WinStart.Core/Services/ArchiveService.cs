using System.IO.Compression;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public sealed class ArchiveService : IArchiveService
{
    private readonly IPathProvider _paths;
    private readonly IProcessRunner _process;

    public ArchiveService(IPathProvider paths, IProcessRunner process)
    {
        _paths = paths;
        _process = process;
    }

    public async Task<bool> ExtractAsync(string archive, string destination, string? password, CancellationToken ct)
    {
        Directory.CreateDirectory(destination);

        var sevenZip = _paths.Tool("7z.exe");
        if (File.Exists(sevenZip))
        {
            var pwd = string.IsNullOrEmpty(password) ? "" : $" -p\"{password}\"";
            var r = await _process.RunAsync(sevenZip,
                $"x -aoa -bso0 -bsp0 \"{archive}\"{pwd} -o\"{destination}\"", ct,
                workingDirectory: _paths.Tools).ConfigureAwait(false);
            if (r.Ok) return true;
        }

        if (!string.IsNullOrEmpty(password)) return false;
        try
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archive, destination, true), ct).ConfigureAwait(false);
            return true;
        }
        catch { return false; }
    }
}
