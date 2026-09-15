namespace WinStart.Core.Models;

public sealed class DiskInfo
{
    public string Name { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
}

public sealed class SystemSummary
{
    public string UserName { get; init; } = Environment.UserName;
    public string AccountName { get; init; } = Environment.UserName;
    public bool IsAdministrator { get; init; }

    public string OsFamily { get; init; } = "Windows";
    public string OsEdition { get; init; } = "";
    public string OsDisplayVersion { get; init; } = "";
    public string OsBuild { get; init; } = "";
    public int Build { get; init; }

    public string CpuName { get; init; } = "";
    public string CpuClock { get; init; } = "";
    public int CpuCores { get; init; }

    public string RamSize { get; init; } = "";
    public string RamType { get; init; } = "";
    public string RamSpeed { get; init; } = "";

    public IReadOnlyList<DiskInfo> Disks { get; init; } = [];

    public string GpuName { get; init; } = "";
    public string Resolution { get; init; } = "";

    public TimeSpan Uptime => TimeSpan.FromMilliseconds(Environment.TickCount64);
}
