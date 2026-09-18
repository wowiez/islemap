using System.Buffers.Binary;
using System.Net;
using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class NpcapGamePacketDecoderTests
{
    [Fact]
    public void MovementPayload_DecodesUnalignedTimestampAndQuantizedVectors()
    {
        var writer = new TestBitWriter();
        writer.Skip(171);
        writer.WriteSingle(190.363f);
        writer.WriteQuantizedVector(3639.1, -27.2, 0, 10);
        writer.WriteQuantizedVector(333668.77, -412585.53, 41068.87, 100);
        writer.WriteCompressedRotator(342.5, 123.75, 0);
        var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());

        var decoded = NpcapGamePacketDecoder.TryReadMovementAt(bunch, 171, out var movement);

        Assert.True(decoded);
        Assert.Equal(190.363f, movement.Timestamp, precision: 3);
        Assert.Equal(333668.77, movement.Location.X, precision: 2);
        Assert.Equal(-412585.53, movement.Location.Y, precision: 2);
        Assert.Equal(41068.87, movement.Location.Z!.Value, precision: 2);
        Assert.Equal(123.75, movement.ControlYawDegrees, precision: 2);
    }

    [Fact]
    public void MovementPayload_RejectsTruncatedData()
    {
        var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, 16, [0xff, 0xff]);

        Assert.False(NpcapGamePacketDecoder.TryReadMovementAt(bunch, 8, out _));
    }

    [Fact]
    public void GamePayload_RejectsMalformedPacketHandlerPayload()
    {
        var decoder = new NpcapGamePacketDecoder();

        Assert.False(decoder.TryProcessGamePayload(new byte[64], DateTimeOffset.UtcNow, out _));
        Assert.False(decoder.TryProcessGamePayload([0x15, 0, 0, 0, 0, 0, 0, 0, 0, 0], DateTimeOffset.UtcNow, out _));
    }

    [Theory]
    [InlineData(0x0c)]
    [InlineData(0x10)]
    [InlineData(0x14)]
    [InlineData(0x18)]
    public void PacketHandlerPrefix_AllowsSessionDependentValues(byte prefix) =>
        Assert.True(NpcapGamePacketDecoder.IsSupportedPacketPrefix(prefix));

    [Fact]
    public void PacketHandlerPrefix_RejectsLowFlagBits() =>
        Assert.False(NpcapGamePacketDecoder.IsSupportedPacketPrefix(0x15));

    [Fact]
    public void Capture_AcceptsInboundServerPacketAddressedToGameLocalPort()
    {
        var frame = BuildUdpFrame("162.222.17.12", 7777, "192.168.1.20", 61319, [0x0c, 0xaa]);

        var accepted = NpcapGamePositionSource.TryReadUdpDatagram(
            frame,
            [61319],
            out var flow,
            out var payload);

        Assert.True(accepted);
        Assert.Equal(61319, flow.LocalPort);
        Assert.Equal(IPAddress.Parse("162.222.17.12"), flow.RemoteAddress);
        Assert.Equal(7777, flow.RemotePort);
        Assert.True(flow.Inbound);
        Assert.Equal([0x0c, 0xaa], payload.ToArray());
    }

    [Fact]
    public void Capture_NormalizesOutboundPacketFromGameLocalPort()
    {
        var frame = BuildUdpFrame("192.168.1.20", 61319, "162.222.17.12", 7777, [0x0c]);

        Assert.True(NpcapGamePositionSource.TryReadUdpDatagram(
            frame,
            [61319],
            out var flow,
            out var payload));
        Assert.Equal(61319, flow.LocalPort);
        Assert.Equal(IPAddress.Parse("162.222.17.12"), flow.RemoteAddress);
        Assert.Equal(7777, flow.RemotePort);
        Assert.False(flow.Inbound);
        Assert.Equal([0x0c], payload.ToArray());
    }

    private static byte[] BuildUdpFrame(
        string sourceAddress,
        int sourcePort,
        string destinationAddress,
        int destinationPort,
        byte[] payload)
    {
        var frame = new byte[14 + 20 + 8 + payload.Length];
        frame[12] = 0x08;
        frame[13] = 0x00;
        const int ip = 14;
        frame[ip] = 0x45;
        frame[ip + 9] = 17;
        IPAddress.Parse(sourceAddress).GetAddressBytes().CopyTo(frame, ip + 12);
        IPAddress.Parse(destinationAddress).GetAddressBytes().CopyTo(frame, ip + 16);
        const int udp = ip + 20;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(udp, 2), (ushort)sourcePort);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(udp + 2, 2), (ushort)destinationPort);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(udp + 4, 2), (ushort)(8 + payload.Length));
        payload.CopyTo(frame, udp + 8);
        return frame;
    }

    private sealed class TestBitWriter
    {
        private readonly List<bool> _bits = [];
        public int Position => _bits.Count;

        public void Skip(int count)
        {
            for (var index = 0; index < count; index++) _bits.Add(false);
        }

        public void WriteSingle(float value) => WriteBytes(BitConverter.GetBytes(value));

        public void WriteQuantizedVector(double x, double y, double z, int scale)
        {
            var values = new[]
            {
                (long)Math.Round(x * scale, MidpointRounding.AwayFromZero),
                (long)Math.Round(y * scale, MidpointRounding.AwayFromZero),
                (long)Math.Round(z * scale, MidpointRounding.AwayFromZero)
            };
            var maximum = values.Max(value => Math.Abs(value));
            var componentBits = Math.Max(1, BitLength(maximum) + 1);
            WriteSerializedInt(componentBits | 0x40, 128);
            foreach (var value in values) WriteSigned(value, componentBits);
        }

        public void WriteCompressedRotator(double pitch, double yaw, double roll)
        {
            WriteCompressedAxis(pitch);
            WriteCompressedAxis(yaw);
            WriteCompressedAxis(roll);
        }

        public byte[] ToArray()
        {
            var result = new byte[(_bits.Count + 7) / 8];
            for (var bit = 0; bit < _bits.Count; bit++) if (_bits[bit]) result[bit >> 3] |= (byte)(1 << (bit & 7));
            return result;
        }

        private void WriteBytes(byte[] bytes)
        {
            foreach (var value in bytes)
                for (var bit = 0; bit < 8; bit++) _bits.Add(((value >> bit) & 1) != 0);
        }

        private void WriteCompressedAxis(double degrees)
        {
            var compressed = (ushort)Math.Round(
                ((degrees % 360d + 360d) % 360d) * 65536d / 360d,
                MidpointRounding.AwayFromZero);
            _bits.Add(compressed != 0);
            if (compressed == 0) return;
            for (var bit = 0; bit < 16; bit++) _bits.Add(((compressed >> bit) & 1) != 0);
        }

        private void WriteSerializedInt(int value, int valueMax)
        {
            var writtenValue = 0;
            var mask = 1;
            while (writtenValue + mask < valueMax)
            {
                var set = (value & mask) != 0;
                _bits.Add(set);
                if (set) writtenValue |= mask;
                mask <<= 1;
            }
        }

        private void WriteSigned(long value, int bitCount)
        {
            var raw = unchecked((ulong)value);
            for (var bit = 0; bit < bitCount; bit++) _bits.Add(((raw >> bit) & 1) != 0);
        }

        private static int BitLength(long value)
        {
            var bits = 0;
            while (value > 0) { bits++; value >>= 1; }
            return bits;
        }
    }
}
