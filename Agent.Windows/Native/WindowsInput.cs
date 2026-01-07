using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Agent.Windows.Native;

internal static class WindowsInput
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public static double GetIdleSeconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info))
        {
            return 0;
        }

        var tickCount = (uint)Environment.TickCount;
        var idleMs = unchecked(tickCount - info.dwTime);
        return idleMs / 1000.0;
    }

    public static ForegroundAppInfo? GetForegroundApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return null;
        }

        string appName;
        try
        {
            appName = Process.GetProcessById((int)pid).ProcessName;
        }
        catch
        {
            return null;
        }

        string? title = null;
        var length = GetWindowTextLength(hwnd);
        if (length > 0)
        {
            var sb = new StringBuilder(length + 1);
            if (GetWindowText(hwnd, sb, sb.Capacity) > 0)
            {
                title = sb.ToString();
            }
        }

        return new ForegroundAppInfo(appName, title);
    }
}

internal sealed record ForegroundAppInfo(string AppName, string? WindowTitle);
