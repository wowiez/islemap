using System.Diagnostics;
using Microsoft.Win32;

namespace TheIsleOverlay.App;

/// <summary>
/// One line that says why the packet capture is or is not running: whether the game
/// process was seen, which UDP ports came with it, whether the Npcap driver service is
/// up and which adapter would be used. The F8 panel shows it so a stuck capture can be
/// diagnosed without a debugger.
/// </summary>
internal static class NpcapDiagnostics
{
    public static string Describe(NpcapSourceState state)
    {
        try
        {
            var status = state.Status;
            var ports = NpcapGamePositionSource.GetGameUdpPortsForDiagnostics();
            var device = NpcapGamePositionSource.GetNpcapDeviceForDiagnostics();
            var service = device is not null ? "npcap OK (mở được card)" : DriverState();
            var portsText = FormatPorts(ports);

            return status switch
            {
                NpcapSourceStatus.Unavailable =>
                    "chưa có npcap.dll (cài Npcap rồi bấm TẢI NPCAP)",
                NpcapSourceStatus.Live =>
                    $"đang bắt packet · cổng {portsText}" +
                    (string.IsNullOrWhiteSpace(state.WeightDiagnostic) ? string.Empty : $" · {state.WeightDiagnostic}"),
                // The source explains how far it got (packets seen? lock found?), which is
                // what tells a stuck capture apart from one that simply has no traffic.
                _ => $"{state.Message} · cổng game: {portsText} · driver: {service} · card: {device ?? "không tìm thấy"}"
            };
        }
        catch (Exception exception)
        {
            return $"không đọc được chẩn đoán: {exception.Message}";
        }
    }

    internal static string FormatPorts(IReadOnlyCollection<int> ports) => ports.Count == 0
        ? "chưa thấy tiến trình game"
        : string.Join(",", ports.Order().Take(6)) +
          (ports.Count > 6 ? $"… ({ports.Count} cổng)" : string.Empty);

    private static string DriverState()
    {
        // Reading the service key avoids the ServiceController package; the installer
        // needs elevation but reading the state does not.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\npcap")
                ?? Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\npf");
            if (key is null)
            {
                return "không thấy service npcap trong registry";
            }

            var start = key.GetValue("Start") as int?;
            return start switch
            {
                2 => "tự động",
                3 => "thủ công",
                4 => "đang tắt",
                _ => "có driver"
            };
        }
        catch (System.Security.SecurityException)
        {
            return "không đọc được (quyền)";
        }
    }

    public static bool GameProcessRunning
    {
        get
        {
            try
            {
                return Process.GetProcessesByName("TheIsleClient-Win64-Shipping").Length > 0 ||
                       Process.GetProcessesByName("TheIsle").Length > 0;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
