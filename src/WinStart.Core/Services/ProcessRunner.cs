using System.Diagnostics;
using System.Globalization;
using System.Text;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Services;

public sealed class ProcessRunner : IProcessRunner
{
    private readonly IPathProvider _paths;
    private readonly ILocalizationService _loc;
    private static readonly Encoding OemEncoding;

    static ProcessRunner()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        try { OemEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage); }
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

            var stdout = proc.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = proc.StandardError.ReadToEndAsync(CancellationToken.None);

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
                (await stdout.ConfigureAwait(false)).Trim(),
                (await stderr.ConfigureAwait(false)).Trim());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new ProcessResult(-1, "", ex.Message);
        }
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
