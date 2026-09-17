using System.Text;
using System.Text.Json;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public enum DnsProvider { Automatic, Google, Cloudflare }

public sealed record NetworkAdapter(int Index, string Name, string Description, string Status,
    string Ipv4Dns, string Ipv6Dns)
{
    public bool IsUp => Status.Equals("Up", StringComparison.OrdinalIgnoreCase);
}

public interface INetworkService
{
    Task<IReadOnlyList<NetworkAdapter>> GetAdaptersAsync(CancellationToken ct);

    Task<string?> SetDnsAsync(NetworkAdapter adapter, DnsProvider provider, CancellationToken ct);
}

public sealed class NetworkService(IProcessRunner process) : INetworkService
{
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
                    x.Ipv4Dns, WithoutDefaults(x.Ipv6Dns)))
                .OrderByDescending(a => a.IsUp)
                .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public async Task<string?> SetDnsAsync(NetworkAdapter adapter, DnsProvider provider, CancellationToken ct)
    {
        var (v4, v6) = AddressesOf(provider);

        var command = provider == DnsProvider.Automatic
            ? $"Set-DnsClientServerAddress -InterfaceIndex {adapter.Index} -ResetServerAddresses -ErrorAction Stop"
            : $"Set-DnsClientServerAddress -InterfaceIndex {adapter.Index} -ServerAddresses " +
              string.Join(",", v4.Concat(v6).Select(a => $"'{a}'")) + " -ErrorAction Stop";

        var r = await process.PowerShellAsync($"{command}\nClear-DnsClientCache", ct, 120000).ConfigureAwait(false);
        if (r.Ok) return null;

        var error = (string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr).Trim();
        return error.Length == 0 ? $"exit code {r.ExitCode}" : error.Split('\n')[0].Trim();
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
        public string Ipv4Dns { get; set; } = "";
        public string Ipv6Dns { get; set; } = "";
    }
}
