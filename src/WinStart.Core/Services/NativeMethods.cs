using System.Runtime.InteropServices;

namespace WinStart.Core.Services;

internal static class NativeMethods
{
    private const int HWND_BROADCAST = 0xFFFF;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint QueryDosDeviceW(string deviceName, char[] targetPath, uint maxLength);

    public static void BroadcastSettingChange(string area)
    {
        try
        {
            SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, area,
                SMTO_ABORTIFHUNG, 1000, out _);
        }
        catch { }
    }

    public static string? QueryDosDevice(string driveLetter)
    {
        try
        {
            var buffer = new char[1024];
            var length = QueryDosDeviceW(driveLetter, buffer, (uint)buffer.Length);
            if (length == 0) return null;
            var text = new string(buffer, 0, (int)length);
            var end = text.IndexOf('\0');
            return end > 0 ? text[..end] : null;
        }
        catch { return null; }
    }
}
