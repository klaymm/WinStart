using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public sealed class DownloadService : IDownloadService, IDisposable
{
    private readonly HttpClient _http;

    public TimeSpan StallTimeout { get; init; } = TimeSpan.FromSeconds(30);

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
        foreach (var url in mirrors)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DownloadCoreAsync(url, destination, progress, 1, Timeout.InfiniteTimeSpan, ct)
                    .ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { }
        }

        return false;
    }

    public Task DownloadFileAsync(string url, string destination, IProgress<double>? progress,
        int attempts, CancellationToken ct) =>
        DownloadCoreAsync(url, destination, progress, attempts, StallTimeout, ct);

    private async Task DownloadCoreAsync(string url, string destination, IProgress<double>? progress,
        int attempts, TimeSpan stallTimeout, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".part";

        try
        {
            if (File.Exists(temp)) File.Delete(temp);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await TransferAsync(url, temp, progress, stallTimeout, ct).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex) when (attempt < attempts && IsTransient(ex, ct))
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct).ConfigureAwait(false);
                }
            }

            if (new FileInfo(temp).Length == 0) throw new IOException("The server returned an empty file.");
            File.Move(temp, destination, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }

        progress?.Report(100);
    }

    private async Task TransferAsync(string url, string temp, IProgress<double>? progress,
        TimeSpan stallTimeout, CancellationToken ct)
    {
        var existing = File.Exists(temp) ? new FileInfo(temp).Length : 0;

        using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct);
        stall.CancelAfter(stallTimeout);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0) request.Headers.Range = new RangeHeaderValue(existing, null);

            using var resp = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token)
                .ConfigureAwait(false);

            if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                File.Delete(temp);
                throw new IOException("The server rejected the resume request.");
            }
            resp.EnsureSuccessStatusCode();

            var resumed = existing > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed) existing = 0;
            var total = resp.Content.Headers.ContentLength is { } length ? length + existing : -1L;

            await using var src = await resp.Content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
            await using var dst = new FileStream(temp, resumed ? FileMode.Append : FileMode.Create,
                FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            var read = existing;
            int n;
            while ((n = await src.ReadAsync(buffer, stall.Token).ConfigureAwait(false)) > 0)
            {
                stall.CancelAfter(stallTimeout);
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0) progress?.Report(read * 100d / total);
            }

            if (total > 0 && read < total)
                throw new IOException($"The connection closed after {read} of {total} bytes.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && stall.IsCancellationRequested)
        {
            throw new TimeoutException($"No data from the server for {stallTimeout.TotalSeconds:0} seconds.");
        }
    }

    public static bool IsTransient(Exception ex, CancellationToken ct) => ex switch
    {
        OperationCanceledException when ct.IsCancellationRequested => false,
        HttpRequestException { StatusCode: { } code } =>
            (int)code >= 500 || code is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests,
        HttpRequestException or IOException or TimeoutException or OperationCanceledException => true,
        _ => false
    };

    public void Dispose() => _http.Dispose();
}
