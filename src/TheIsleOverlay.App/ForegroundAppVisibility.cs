using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TheIsleOverlay.App;

internal static class ForegroundAppVisibility
{
    private static readonly HashSet<string> AllowedGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "TheIsle",
        "TheIsleClient-Win64-Shipping"
    };

    public static bool ShouldShowOverlay()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == (uint)Environment.ProcessId) return true;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return IsAllowedProcessName(process.ProcessName);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static bool IsAllowedProcessName(string? processName) =>
        !string.IsNullOrWhiteSpace(processName) && AllowedGameProcesses.Contains(processName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
