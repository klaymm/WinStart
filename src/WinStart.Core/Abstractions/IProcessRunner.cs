namespace WinStart.Core.Abstractions;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
}

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string file, string arguments, CancellationToken ct,
        string? workingDirectory = null, int timeoutMs = 0);

    Task<ProcessResult> CmdAsync(string commandLine, CancellationToken ct, int timeoutMs = 0);

    Task<ProcessResult> PowerShellAsync(string script, CancellationToken ct, int timeoutMs = 0);

    Task<ProcessResult> TrustedInstallerAsync(string commandLine, CancellationToken ct, int timeoutMs = 0);

    Task<ProcessResult> LaunchAsync(string file, string arguments, bool wait, CancellationToken ct,
        string? workingDirectory = null);
}
