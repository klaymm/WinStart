using System.Globalization;
using System.Management;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;

namespace WinStart.Core.Services;

public interface ISystemInfoService
{
    int Build { get; }

    Task<SystemSummary> GetSummaryAsync(CancellationToken ct);
}

public sealed class SystemInfoService : ISystemInfoService
{
    private const string CurrentVersionKey = @"HKLM\Software\Microsoft\Windows NT\CurrentVersion";

    private readonly IRegistryService _registry;
    private readonly Lazy<Task<SystemSummary>> _summary;

    public SystemInfoService(IRegistryService registry)
    {
        _registry = registry;

        var buildText = _registry.GetString(CurrentVersionKey, "CurrentBuildNumber");
        Build = int.TryParse(buildText, out var parsed) ? parsed : Environment.OSVersion.Version.Build;

        _summary = new Lazy<Task<SystemSummary>>(() => Task.Run(Collect));
    }

    public int Build { get; }

    private bool IsWindows11 => Build >= 22000;

    public Task<SystemSummary> GetSummaryAsync(CancellationToken ct) => _summary.Value.WaitAsync(ct);

    private static string GetUserDisplayName()
    {
        try
        {
            var name = Environment.UserName.Replace("'", "''");
            var full = GetWmiString("Win32_UserAccount",
                $"Name='{name}' AND Domain='{Environment.MachineName}'", "FullName");
            if (!string.IsNullOrWhiteSpace(full)) return full.Split(' ')[0];
        }
        catch { }

        return Environment.UserName;
    }

    private SystemSummary Collect()
    {
        var ubr = _registry.GetInt(CurrentVersionKey, "UBR");
        var productName = _registry.GetString(CurrentVersionKey, "ProductName") ?? "Windows";

        if (IsWindows11) productName = productName.Replace("Windows 10", "Windows 11");

        var (cpuName, clock, cores) = GetCpu();
        var (ramSize, ramType, ramSpeed) = GetRam();

        return new SystemSummary
        {
            UserName = GetUserDisplayName(),
            AccountName = Environment.UserName,
            IsAdministrator = IsElevated(),
            Resolution = GetResolution(),
            Disks = GetAllDisks(),
            OsFamily = IsWindows11 ? "Windows 11" : "Windows 10",
            OsEdition = productName,
            OsDisplayVersion = _registry.GetString(CurrentVersionKey, "DisplayVersion")
                               ?? _registry.GetString(CurrentVersionKey, "ReleaseId") ?? "",
            OsBuild = ubr is > 0 ? $"{Build}.{ubr}" : Build.ToString(CultureInfo.InvariantCulture),
            Build = Build,

            CpuName = cpuName,
            CpuClock = clock,
            CpuCores = cores,

            RamSize = ramSize,
            RamType = ramType,
            RamSpeed = ramSpeed,

            GpuName = GetWmiString("Win32_VideoController", null, "Name")
        };
    }

    private static (string Name, string Clock, int Cores) GetCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, MaxClockSpeed, NumberOfCores FROM Win32_Processor");

            foreach (var o in searcher.Get().Cast<ManagementObject>())
            {
                var name = Clean(o["Name"]?.ToString());
                var mhz = Convert.ToDouble(o["MaxClockSpeed"] ?? 0, CultureInfo.InvariantCulture);
                var cores = Convert.ToInt32(o["NumberOfCores"] ?? 0, CultureInfo.InvariantCulture);
                return (name, $"{(mhz / 1000).ToString("0.0#", CultureInfo.InvariantCulture)} GHz", cores);
            }
        }
        catch { }

        return (Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "CPU", "",
            Environment.ProcessorCount);
    }

    private static (string Size, string Type, string Speed) GetRam()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory");

            ulong total = 0;
            uint speed = 0, type = 0;

            foreach (var o in searcher.Get().Cast<ManagementObject>())
            {
                total += Convert.ToUInt64(o["Capacity"] ?? 0UL, CultureInfo.InvariantCulture);

                var cfg = Convert.ToUInt32(o["ConfiguredClockSpeed"] ?? 0U, CultureInfo.InvariantCulture);
                if (cfg == 0) cfg = Convert.ToUInt32(o["Speed"] ?? 0U, CultureInfo.InvariantCulture);
                if (cfg > speed) speed = cfg;

                if (type == 0) type = Convert.ToUInt32(o["SMBIOSMemoryType"] ?? 0U, CultureInfo.InvariantCulture);
            }

            if (total > 0)
            {
                var gb = Math.Round(total / 1024d / 1024 / 1024);
                return ($"{gb.ToString("0", CultureInfo.InvariantCulture)} GB", MemoryType(type),
                    speed > 0 ? $"{speed} MHz" : "");
            }
        }
        catch { }

        return ("", "", "");
    }

    private static string MemoryType(uint code) => code switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        35 => "LPDDR4",
        36 => "LPDDR5",
        _ => ""
    };

    private static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string GetResolution()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController");
            foreach (var o in searcher.Get().Cast<ManagementObject>())
            {
                var w = Convert.ToInt32(o["CurrentHorizontalResolution"] ?? 0, CultureInfo.InvariantCulture);
                var h = Convert.ToInt32(o["CurrentVerticalResolution"] ?? 0, CultureInfo.InvariantCulture);
                if (w > 0 && h > 0) return $"{w}x{h}";
            }
        }
        catch { }
        return "";
    }

    private static IReadOnlyList<DiskInfo> GetAllDisks()
    {
        var list = new List<DiskInfo>();

        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { return list; }

        foreach (var d in drives)
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new DiskInfo
                {
                    Name = d.Name.TrimEnd('\\'),
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.AvailableFreeSpace
                });
            }
            catch { }
        }

        return list;
    }

    private static string GetWmiString(string wmiClass, string? where, string property)
    {
        try
        {
            var query = $"SELECT {property} FROM {wmiClass}" + (where is null ? "" : $" WHERE {where}");
            using var searcher = new ManagementObjectSearcher(query);

            foreach (var o in searcher.Get().Cast<ManagementObject>())
            {
                var value = Clean(o[property]?.ToString());
                if (value.Length > 0) return value;
            }
        }
        catch { }

        return "";
    }

    private static string Clean(string? s) =>
        string.Join(' ', (s ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
