using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace TheIsleOverlay.App;

internal sealed class NpcapGamePositionSource : IAsyncDisposable
{
    private const string NpcapLibrary = @"C:\Windows\System32\Npcap\wpcap.dll";
    private static readonly string[] GameProcessNames =
    [
        "TheIsleClient-Win64-Shipping",
        "TheIsle"
    ];

    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<GameFlow, NpcapGamePacketDecoder> _decoders = [];
    private readonly NpcapPositionStabilizer _stabilizer = new();
    private Task? _captureTask;
    private NpcapSourceState _state;
    private long _lastPositionTick;
    private GameFlow? _activeFlow;

    public NpcapGamePositionSource()
    {
        _state = IsSupported
            ? new NpcapSourceState(NpcapSourceStatus.Stopped, "Sẵn sàng")
            : new NpcapSourceState(NpcapSourceStatus.Unavailable, "Chưa cài Npcap");
    }

    public event Action<NpcapPositionSample>? PositionReceived;
    public event Action<NpcapSourceState>? StateChanged;

    public bool IsSupported => OperatingSystem.IsWindows() && File.Exists(NpcapLibrary);
    public NpcapSourceState State => _state;

    internal static IReadOnlyList<int> GetGameUdpPortsForDiagnostics() =>
        FindGameUdpEndpoints().Select(endpoint => endpoint.Port).Distinct().Order().ToArray();

    internal static string? GetNpcapDeviceForDiagnostics() => FindActiveNpcapDevice();

    public void Start()
    {
        if (!IsSupported || _captureTask is not null) return;
        SetState(NpcapSourceStatus.WaitingForGame, "Đang tìm game và kết nối UDP");
        _captureTask = Task.Run(() => CaptureLoopAsync(_shutdown.Token));
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var gameEndpoints = FindGameUdpEndpoints();
                var device = FindActiveNpcapDevice();
                if (gameEndpoints.Count == 0)
                {
                    SetState(NpcapSourceStatus.WaitingForGame, "Đang chờ The Isle");
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                    continue;
                }
                if (device is null)
                {
                    SetState(
                        NpcapSourceStatus.Faulted,
                        "Npcap driver chưa chạy · bấm MỞ LẠI và chấp nhận UAC");
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                SetState(NpcapSourceStatus.Listening, "Đã bắt luồng game · đang chờ tọa độ");
                _activeFlow = null;
                _stabilizer.Reset();
                Capture(device, gameEndpoints, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
            {
                SetState(NpcapSourceStatus.Faulted, "Npcap không tương thích hoặc thiếu DLL");
                break;
            }
            catch (Exception ex)
            {
                SetState(NpcapSourceStatus.Faulted, $"Npcap lỗi: {ex.Message}");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private void Capture(string device, IReadOnlyList<GameUdpEndpoint> endpoints, CancellationToken cancellationToken)
    {
        var errors = new StringBuilder(512);
        var handle = Native.pcap_open_live(device, 65535, 0, 200, errors);
        if (handle == IntPtr.Zero)
        {
            SetState(NpcapSourceStatus.Faulted, CleanError(errors, "Không mở được card mạng"));
            return;
        }
        try
        {
            var filter = new Native.BpfProgram();
            var ports = endpoints.Select(endpoint => endpoint.Port).Distinct().Order().ToArray();
            // Capture both server replication (inbound) and client movement RPCs
            // (outbound). They use different packet-handler layouts, so the
            // normalized GameFlow below keeps each direction on its own decoder.
            var filterText = "udp and (" + string.Join(" or ", ports.Select(port => $"port {port}")) + ")";
            if (Native.pcap_compile(handle, ref filter, filterText, 1, 0xffffffff) != 0)
            {
                SetState(NpcapSourceStatus.Faulted, "Không tạo được bộ lọc gói tin game");
                return;
            }
            try
            {
                if (Native.pcap_setfilter(handle, ref filter) != 0)
                {
                    SetState(NpcapSourceStatus.Faulted, "Không áp dụng được bộ lọc gói tin game");
                    return;
                }
                var lastProcessCheck = Environment.TickCount64;
                var lastCaptureStatus = lastProcessCheck;
                long gameDatagrams = 0;
                while (!cancellationToken.IsCancellationRequested)
                {
                    var result = Native.pcap_next_ex(handle, out var headerPointer, out var dataPointer);
                    if (result < 0) return;
                    if (result > 0)
                    {
                        var header = Marshal.PtrToStructure<Native.PcapPacketHeader>(headerPointer);
                        if (header.CapturedLength is > 0 and <= 65535)
                        {
                            var frame = new byte[header.CapturedLength];
                            Marshal.Copy(dataPointer, frame, 0, frame.Length);
                            var capturedAt = DateTimeOffset.FromUnixTimeSeconds(header.Timestamp.Seconds)
                                .AddTicks(header.Timestamp.Microseconds * 10L);
                            if (!cancellationToken.IsCancellationRequested &&
                                TryReadUdpDatagram(frame, ports, out var flow, out var payload))
                            {
                                gameDatagrams++;
                                if (GetDecoder(flow).TryProcessGamePayload(payload, capturedAt, out var sample) &&
                                    AcceptFlow(flow) &&
                                    _stabilizer.TryStabilize(
                                        sample with
                                        {
                                            Location = NpcapGameCoordinateTransform.ToAssetLocation(sample.Location)
                                        },
                                        out var stabilized))
                                {
                                    _lastPositionTick = Environment.TickCount64;
                                    SetState(
                                        NpcapSourceStatus.Live,
                                        $"Đang chạy · server {flow.RemoteAddress}:{flow.RemotePort}");
                                    PositionReceived?.Invoke(stabilized);
                                }
                            }
                        }
                    }

                    if (_lastPositionTick == 0 && Environment.TickCount64 - lastCaptureStatus > 2000)
                    {
                        SetState(
                            NpcapSourceStatus.Listening,
                            gameDatagrams == 0
                                ? "Đã mở card mạng · chưa nhận gói game"
                                : $"Đã nhận {gameDatagrams} gói UDP · đang tìm tọa độ");
                        lastCaptureStatus = Environment.TickCount64;
                    }

                    if (Environment.TickCount64 - lastProcessCheck > 3000)
                    {
                        var current = FindGameUdpEndpoints();
                        if (current.Count == 0 || !ports.SequenceEqual(current.Select(endpoint => endpoint.Port).Distinct().Order())) return;
                        lastProcessCheck = Environment.TickCount64;
                    }
                }
            }
            finally
            {
                Native.pcap_freecode(ref filter);
            }
        }
        finally
        {
            Native.pcap_close(handle);
        }
    }

    private bool AcceptFlow(GameFlow flow)
    {
        if (_activeFlow is null)
        {
            _activeFlow = flow;
            return true;
        }

        if (_activeFlow.Value == flow)
        {
            return true;
        }

        // Inbound replication contains movement for every nearby actor. The
        // client's outbound movement RPC is the only reliable source for the
        // local player. Prefer it immediately when it becomes available,
        // otherwise a stationary player can be replaced by another dino's
        // packet and appear to jump across the map.
        if (!_activeFlow.Value.Inbound && flow.Inbound)
        {
            return false;
        }

        if (_activeFlow.Value.Inbound && !flow.Inbound)
        {
            _activeFlow = flow;
            _stabilizer.Reset();
            return true;
        }

        if (Environment.TickCount64 - _lastPositionTick <= NpcapPositionStabilizer.SourceHoldDuration.TotalMilliseconds)
        {
            return false;
        }

        _activeFlow = flow;
        _stabilizer.Reset();
        return true;
    }

    private void SetState(NpcapSourceStatus status, string message)
    {
        var next = new NpcapSourceState(status, message);
        if (next == _state) return;
        _state = next;
        StateChanged?.Invoke(next);
    }

    private static string CleanError(StringBuilder error, string fallback)
    {
        var value = error.ToString().Trim();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private NpcapGamePacketDecoder GetDecoder(GameFlow flow)
    {
        if (_decoders.TryGetValue(flow, out var decoder)) return decoder;
        decoder = new NpcapGamePacketDecoder();
        _decoders[flow] = decoder;
        return decoder;
    }

    internal static bool TryReadUdpDatagram(
        byte[] frame,
        IReadOnlyCollection<int> localPorts,
        out GameFlow flow,
        out ReadOnlySpan<byte> payload)
    {
        flow = default;
        payload = default;
        if (frame.Length < 42) return false;
        var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(12, 2));
        var ipOffset = etherType == 0x8100 ? 18 : 14;
        if (frame.Length < ipOffset + 28 || frame[ipOffset] >> 4 != 4 || frame[ipOffset + 9] != 17) return false;
        var ipHeaderLength = (frame[ipOffset] & 0x0f) * 4;
        var udpOffset = ipOffset + ipHeaderLength;
        if (frame.Length < udpOffset + 8) return false;
        var sourcePort = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(udpOffset, 2));
        var destinationPort = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(udpOffset + 2, 2));
        var udpLength = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(udpOffset + 4, 2));
        var payloadLength = Math.Max(0, Math.Min(udpLength - 8, frame.Length - udpOffset - 8));
        var source = new IPAddress(frame.AsSpan(ipOffset + 12, 4));
        var destination = new IPAddress(frame.AsSpan(ipOffset + 16, 4));
        if (localPorts.Contains(destinationPort))
        {
            flow = new GameFlow(destinationPort, source, sourcePort, Inbound: true);
        }
        else if (localPorts.Contains(sourcePort))
        {
            flow = new GameFlow(sourcePort, destination, destinationPort, Inbound: false);
        }
        else
        {
            return false;
        }
        payload = frame.AsSpan(udpOffset + 8, payloadLength);
        return true;
    }

    internal static async Task<bool> TryStartDriverElevatedAsync()
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(NpcapLibrary)) return false;
        if (FindActiveNpcapDevice() is not null) return true;

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "sc.exe"),
                Arguments = "start npcap",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process is null) return false;
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            for (var attempt = 0; attempt < 10; attempt++)
            {
                if (FindActiveNpcapDevice() is not null) return true;
                await Task.Delay(200).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException or TimeoutException)
        {
            // UAC cancellation or a locked service is reported through the
            // source status after the caller restarts capture.
        }
        return false;
    }

    private static IReadOnlyList<GameUdpEndpoint> FindGameUdpEndpoints()
    {
        var processIds = new HashSet<int>();
        foreach (var name in GameProcessNames)
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process) processIds.Add(process.Id);
            }
        }
        if (processIds.Count == 0) return [];

        return ReadUdpEndpoints()
            .Where(endpoint => processIds.Contains(endpoint.ProcessId) && endpoint.Port > 0)
            .ToArray();
    }

    private static IReadOnlyList<GameUdpEndpoint> ReadUdpEndpoints()
    {
        var size = 0;
        var first = Native.GetExtendedUdpTable(IntPtr.Zero, ref size, false, 2, Native.UdpTableClass.OwnerPid, 0);
        if (first != Native.ErrorInsufficientBuffer || size <= 0) return [];
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (Native.GetExtendedUdpTable(buffer, ref size, false, 2, Native.UdpTableClass.OwnerPid, 0) != 0) return [];
            var count = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<Native.UdpRowOwnerPid>();
            var rows = new List<GameUdpEndpoint>(Math.Max(0, count));
            var rowPointer = IntPtr.Add(buffer, sizeof(uint));
            for (var index = 0; index < count; index++)
            {
                var row = Marshal.PtrToStructure<Native.UdpRowOwnerPid>(IntPtr.Add(rowPointer, index * rowSize));
                var address = new IPAddress(row.LocalAddress);
                var port = (ushort)IPAddress.NetworkToHostOrder((short)(row.LocalPort & 0xffff));
                rows.Add(new GameUdpEndpoint((int)row.ProcessId, address, port));
            }
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static string? FindActiveNpcapDevice()
    {
        IPAddress? routedAddress = null;
        try
        {
            using var routeProbe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            routeProbe.Connect(IPAddress.Parse("1.1.1.1"), 53);
            routedAddress = (routeProbe.LocalEndPoint as IPEndPoint)?.Address;
        }
        catch (SocketException)
        {
        }

        var adapterIds = NetworkInterface.GetAllNetworkInterfaces()
            .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up &&
                              adapter.NetworkInterfaceType is not NetworkInterfaceType.Loopback and
                              not NetworkInterfaceType.Tunnel &&
                              adapter.GetIPProperties().UnicastAddresses.Any(address =>
                                  address.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                                  !IPAddress.IsLoopback(address.Address)))
            .OrderByDescending(adapter => routedAddress is not null &&
                adapter.GetIPProperties().UnicastAddresses.Any(address => address.Address.Equals(routedAddress)))
            .ThenByDescending(adapter => adapter.GetIPProperties().GatewayAddresses.Count > 0)
            .Select(adapter => adapter.Id)
            .ToArray();
        if (adapterIds.Length == 0) return null;

        var errors = new StringBuilder(512);
        if (Native.pcap_findalldevs(out var head, errors) != 0) return null;
        try
        {
            foreach (var adapterId in adapterIds)
            {
                for (var current = head; current != IntPtr.Zero;)
                {
                    var item = Marshal.PtrToStructure<Native.PcapInterface>(current);
                    var name = Marshal.PtrToStringAnsi(item.Name) ?? string.Empty;
                    if (name.Contains(adapterId, StringComparison.OrdinalIgnoreCase)) return name;
                    current = item.Next;
                }
            }
            return null;
        }
        finally
        {
            Native.pcap_freealldevs(head);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_captureTask is not null)
        {
            try { await _captureTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        SetState(NpcapSourceStatus.Stopped, "Đã dừng");
        _shutdown.Dispose();
    }

    private readonly record struct GameUdpEndpoint(int ProcessId, IPAddress Address, int Port);
    internal readonly record struct GameFlow(
        int LocalPort,
        IPAddress RemoteAddress,
        int RemotePort,
        bool Inbound);

    private static class Native
    {
        internal const int ErrorInsufficientBuffer = 122;

        [StructLayout(LayoutKind.Sequential)]
        internal struct TimeValue { public int Seconds; public int Microseconds; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PcapPacketHeader
        {
            public TimeValue Timestamp;
            public int CapturedLength;
            public int OriginalLength;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BpfProgram { public uint Length; public IntPtr Instructions; }

        [StructLayout(LayoutKind.Sequential)]
        internal struct PcapInterface
        {
            public IntPtr Next;
            public IntPtr Name;
            public IntPtr Description;
            public IntPtr Addresses;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct UdpRowOwnerPid
        {
            public uint LocalAddress;
            public uint LocalPort;
            public uint ProcessId;
        }

        internal enum UdpTableClass { Basic, OwnerPid, OwnerModule }

        [DllImport(NpcapLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern IntPtr pcap_open_live(string device, int snaplen, int promiscuous, int timeoutMs, StringBuilder errorBuffer);

        [DllImport(NpcapLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int pcap_findalldevs(out IntPtr devices, StringBuilder errorBuffer);

        [DllImport(NpcapLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void pcap_freealldevs(IntPtr devices);

        [DllImport(NpcapLibrary, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        internal static extern int pcap_compile(IntPtr handle, ref BpfProgram program, string filter, int optimize, uint netmask);

        [DllImport(NpcapLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int pcap_setfilter(IntPtr handle, ref BpfProgram program);

        [DllImport(NpcapLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int pcap_next_ex(IntPtr handle, out IntPtr header, out IntPtr data);

        [DllImport(NpcapLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void pcap_freecode(ref BpfProgram program);

        [DllImport(NpcapLibrary, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void pcap_close(IntPtr handle);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        internal static extern int GetExtendedUdpTable(
            IntPtr table,
            ref int size,
            bool order,
            int addressFamily,
            UdpTableClass tableClass,
            uint reserved);
    }
}

public enum NpcapSourceStatus
{
    Unavailable,
    Stopped,
    WaitingForGame,
    Listening,
    Live,
    Faulted
}

public sealed record NpcapSourceState(NpcapSourceStatus Status, string Message)
{
    public bool IsActive => Status is NpcapSourceStatus.WaitingForGame or
        NpcapSourceStatus.Listening or NpcapSourceStatus.Live;
}
