using Microsoft.Win32;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public interface IRestorePointService
{
    Task<string?> CreateAsync(string description, CancellationToken ct);
}

public sealed class RestorePointService(IProcessRunner process, IRegistryService registry) : IRestorePointService
{
    private const string SystemRestoreKey = @"HKLM\Software\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    public async Task<string?> CreateAsync(string description, CancellationToken ct)
    {
        registry.SetValue(SystemRestoreKey, "SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);

        var name = description.Replace("'", "''");
        var r = await process.PowerShellAsync(
            $"Checkpoint-Computer -Description '{name}' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop",
            ct, 600000).ConfigureAwait(false);

        if (r.Ok) return null;

        var error = string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr;
        return string.IsNullOrWhiteSpace(error) ? $"exit code {r.ExitCode}" : error.Trim();
    }
}
