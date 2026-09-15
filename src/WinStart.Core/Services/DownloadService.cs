using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public sealed class DownloadService : IDownloadService, IDisposable
{
    private readonly HttpClient _http;

    public DownloadService()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(30) };
        var version = typeof(DownloadService).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"WinStart/{version}");
    }

    public async Task<bool> IsOnlineAsync(CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 1500).ConfigureAwait(false);
            if (reply.Status == IPStatus.Success) return true;
        }
        catch { }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(5000);
            using var req = new HttpRequestMessage(HttpMethod.Head, "https://www.microsoft.com");
            using var resp = await _http.SendAsync(req, cts.Token).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<bool> DownloadAsync(IEnumerable<string> mirrors, string destination,
        IProgress<double>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        foreach (var url in mirrors)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await TryDownloadAsync(url, destination, progress, ct).ConfigureAwait(false))
                    return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        return false;
    }

    private async Task<bool> TryDownloadAsync(string url, string destination,
        IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return false;

        var total = resp.Content.Headers.ContentLength ?? -1L;
        var temp = destination + ".part";

        try
        {
            await using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await using (var dst = File.Create(temp))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    read += n;
                    if (total > 0) progress?.Report(read * 100d / total);
                }
            }

            if (new FileInfo(temp).Length == 0) return false;

            File.Move(temp, destination, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }

        progress?.Report(100);
        return true;
    }

    public void Dispose() => _http.Dispose();
}
