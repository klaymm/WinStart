using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public enum DnsProvider { Automatic, Google, Cloudflare }

public enum DohMode { Off, Preferred, Required }

public sealed record NetworkAdapter(int Index, string Name, string Description, string Status,
    string Ipv4Dns, string Ipv6Dns, string InterfaceGuid, DohMode Doh)
{
    public bool IsUp => Status.Equals("Up", StringComparison.OrdinalIgnoreCase);
}

public interface INetworkService
{
    Task<IReadOnlyList<NetworkAdapter>> GetAdaptersAsync(CancellationToken ct);

    Task<string?> SetDnsAsync(NetworkAdapter adapter, DnsProvider provider, DohMode doh, CancellationToken ct);
}

public sealed class NetworkService(IProcessRunner process) : INetworkService
{
    private const string InterfacesPath = @"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters";
    private const long DohAutoTemplate = 0x1;
    private const long DohFallback = 0x4;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<DnsProvider, (string[] V4, string[] V6)> Servers = new()
    {
        [DnsProvider.Google] = (["8.8.8.8", "8.8.4.4"], ["2001:4860:4860::8888", "2001:4860:4860::8844"]),
        [DnsProvider.Cloudflare] = (["1.1.1.1", "1.0.0.1"], ["2606:4700:4700::1111", "2606:4700:4700::1001"])
    };

    public static (string[] V4, string[] V6) AddressesOf(DnsProvider provider) =>
        Servers.TryGetValue(provider, out var pair) ? pair : ([], []);

    public async Task<IReadOnlyList<NetworkAdapter>> GetAdaptersAsync(CancellationToken ct)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            $rows = Get-NetAdapter | ForEach-Object {
              $v4 = (Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv4).ServerAddresses -join ', '
              $v6 = (Get-DnsClientServerAddress -InterfaceIndex $_.ifIndex -AddressFamily IPv6).ServerAddresses -join ', '
              [pscustomobject]@{
                Index = [int]$_.ifIndex
                Name = [string]$_.Name
                Description = [string]$_.InterfaceDescription
                Status = [string]$_.Status
                Guid = [string]$_.InterfaceGuid
                Ipv4Dns = [string]$v4
                Ipv6Dns = [string]$v6
              }
            }
            $json = ConvertTo-Json -InputObject @($rows) -Compress -Depth 3
            'B64:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
            """;

        var r = await process.PowerShellAsync(script, ct, 120000).ConfigureAwait(false);
        var marker = r.StdOut.IndexOf("B64:", StringComparison.Ordinal);
        if (marker < 0) return [];

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(r.StdOut[(marker + 4)..].Trim()));
            var rows = JsonSerializer.Deserialize<List<Row>>(json, JsonOptions) ?? [];
            return rows
                .Where(x => !x.Status.Equals("Not Present", StringComparison.OrdinalIgnoreCase))
                .Select(x => new NetworkAdapter(x.Index, x.Name, x.Description, x.Status,
                    x.Ipv4Dns, WithoutDefaults(x.Ipv6Dns), x.Guid, ReadDoh(x.Guid, x.Ipv4Dns)))
                .OrderByDescending(a => a.IsUp)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<string?> SetDnsAsync(NetworkAdapter adapter, DnsProvider provider, DohMode doh, CancellationToken ct)
    {
        var (v4, v6) = AddressesOf(provider);

        var command = provider == DnsProvider.Automatic
            ? $"Set-DnsClientServerAddress -InterfaceIndex {adapter.Index} -ResetServerAddresses -ErrorAction Stop"
            : $"Set-DnsClientServerAddress -InterfaceIndex {adapter.Index} -ServerAddresses " +
              string.Join(",", v4.Concat(v6).Select(a => $"'{a}'")) + " -ErrorAction Stop";

        var r = await process.PowerShellAsync(command, ct, 120000).ConfigureAwait(false);
        if (!r.Ok)
        {
            var error = (string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr).Trim();
            return error.Length == 0 ? $"exit code {r.ExitCode}" : error.Split('\n')[0].Trim();
        }

        string? dohError = null;
        try
        {
            WriteDoh(adapter.InterfaceGuid, provider == DnsProvider.Automatic ? DohMode.Off : doh, v4, v6);
        }
        catch (Exception ex)
        {
            dohError = ex.Message;
        }

        await process.RunAsync("ipconfig.exe", "/flushdns", ct, timeoutMs: 30000).ConfigureAwait(false);
        return dohError;
    }

    private static void WriteDoh(string interfaceGuid, DohMode doh, string[] v4, string[] v6)
    {
        if (string.IsNullOrWhiteSpace(interfaceGuid)) return;

        var settings = $@"{InterfacesPath}\{interfaceGuid}\DohInterfaceSettings";
        Registry.LocalMachine.DeleteSubKeyTree(settings, throwOnMissingSubKey: false);
        if (doh == DohMode.Off) return;

        var flags = doh == DohMode.Required ? DohAutoTemplate : DohAutoTemplate | DohFallback;
        foreach (var (family, addresses) in new[] { ("Doh", v4), ("Doh6", v6) })
        {
            foreach (var address in addresses)
            {
                using var key = Registry.LocalMachine.CreateSubKey($@"{settings}\{family}\{address}", writable: true);
                key.SetValue("DohFlags", flags, RegistryValueKind.QWord);
            }
        }
    }

    private static DohMode ReadDoh(string interfaceGuid, string ipv4Servers)
    {
        if (string.IsNullOrWhiteSpace(interfaceGuid)) return DohMode.Off;

        try
        {
            var modes = ipv4Servers
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(address =>
                {
                    using var key = Registry.LocalMachine.OpenSubKey(
                        $@"{InterfacesPath}\{interfaceGuid}\DohInterfaceSettings\Doh\{address}");
                    return key?.GetValue("DohFlags") is long flags && (flags & (DohAutoTemplate | 0x2)) != 0
                        ? (flags & DohFallback) != 0 ? DohMode.Preferred : DohMode.Required
                        : DohMode.Off;
                })
                .ToList();

            return modes.Count == 0 || modes.Any(m => m == DohMode.Off) ? DohMode.Off
                : modes.All(m => m == DohMode.Required) ? DohMode.Required
                : DohMode.Preferred;
        }
        catch
        {
            return DohMode.Off;
        }
    }

    private static string WithoutDefaults(string servers) =>
        string.Join(", ", servers
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(a => !a.StartsWith("fec0:0:0:ffff::", StringComparison.OrdinalIgnoreCase)));

    private sealed class Row
    {
        public int Index { get; set; }
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string Status { get; set; } = "";
        public string Guid { get; set; } = "";
        public string Ipv4Dns { get; set; } = "";
        public string Ipv6Dns { get; set; } = "";
    }
}
