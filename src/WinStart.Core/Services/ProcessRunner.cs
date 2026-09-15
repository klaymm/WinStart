using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly IPathProvider _paths;
    private readonly ILocalizationService _loc;
    private static readonly Encoding OemEncoding;

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    static ProcessRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { OemEncoding = Encoding.GetEncoding((int)GetOEMCP()); }
        catch { OemEncoding = Encoding.UTF8; }
    }

    public ProcessRunner(IPathProvider paths, ILocalizationService loc)
    {
        _paths = paths;
        _loc = loc;
    }

    public async Task<ProcessResult> RunAsync(string file, string arguments, CancellationToken ct,
        string? workingDirectory = null, int timeoutMs = 0)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = OemEncoding,
            StandardErrorEncoding = OemEncoding,
            WorkingDirectory = workingDirectory ?? Environment.SystemDirectory
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return new ProcessResult(-1, "", _loc.Format("log.startFailed", file));

            var stdout = ReadAllAsync(proc.StandardOutput.BaseStream);
            var stderr = ReadAllAsync(proc.StandardError.BaseStream);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeoutMs > 0) cts.CancelAfter(timeoutMs);

            try { await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                TryKill(proc);
                if (ct.IsCancellationRequested) throw;
                return new ProcessResult(-2, "", _loc.Format("log.timeout", file));
            }

            return new ProcessResult(proc.ExitCode,
                Decode(await stdout.ConfigureAwait(false)).Trim(),
                Decode(await stderr.ConfigureAwait(false)).Trim());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", ex.Message);
        }
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2).Replace("\0", "");

        var sample = Math.Min(bytes.Length, 512);
        int odd = 0, zeros = 0;
        for (var i = 1; i < sample; i += 2)
        {
            odd++;
            if (bytes[i] == 0) zeros++;
        }

        return odd > 0 && zeros * 10 >= odd * 4
            ? Encoding.Unicode.GetString(bytes).Replace("\0", "")
            : OemEncoding.GetString(bytes);
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(true); } catch { }
    }

    public Task<ProcessResult> CmdAsync(string commandLine, CancellationToken ct, int timeoutMs = 0)
        => RunAsync("cmd.exe", $"/d /c {commandLine}", ct, timeoutMs: timeoutMs);

    public Task<ProcessResult> PowerShellAsync(string script, CancellationToken ct, int timeoutMs = 0)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return RunAsync("powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encoded}", ct, timeoutMs: timeoutMs);
    }

    public async Task<ProcessResult> TrustedInstallerAsync(string commandLine, CancellationToken ct, int timeoutMs = 0)
    {
        var nsudo = _paths.Tool("NSudoLG.exe");
        if (!File.Exists(nsudo))
            return await CmdAsync(commandLine, ct, timeoutMs).ConfigureAwait(false);

        await RunAsync("sc.exe", "start TrustedInstaller", ct, timeoutMs: 15000).ConfigureAwait(false);

        return await RunAsync(nsudo,
            $"-U:T -P:E -ShowWindowMode:Hide -Wait cmd.exe /d /c {commandLine}", ct,
            workingDirectory: _paths.Tools, timeoutMs: timeoutMs).ConfigureAwait(false);
    }

    public async Task<ProcessResult> LaunchAsync(string file, string arguments, bool wait, CancellationToken ct,
        string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(file) ?? Environment.SystemDirectory
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null) return new ProcessResult(-1, "", _loc.Format("log.startFailed", file));
            if (!wait) return new ProcessResult(0, "", "");

            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            return new ProcessResult(proc.ExitCode, "", "");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", ex.Message);
        }
    }
}
