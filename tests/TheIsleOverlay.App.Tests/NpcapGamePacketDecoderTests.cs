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
        Assert.Equal(123.75, movement.ControlYawDegrees!.Value, precision: 2);
    }

    [Fact]
    public void MovementPayload_DecodesZeroDegreeNorthRotation()
    {
        var writer = new TestBitWriter();
        writer.Skip(171);
        writer.WriteSingle(190.363f);
        writer.WriteQuantizedVector(3639.1, -27.2, 0, 10);
        writer.WriteQuantizedVector(333668.77, -412585.53, 41068.87, 100);
        writer.WriteCompressedRotator(0, 180, 0); // 0 pitch, 180 yaw (North in UE), 0 roll
        var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());

        var decoded = NpcapGamePacketDecoder.TryReadMovementAt(bunch, 171, out var movement);

        Assert.True(decoded);
        Assert.NotNull(movement.ControlYawDegrees);
        Assert.Equal(180.0, movement.ControlYawDegrees.Value, precision: 2);
    }

    [Fact]
    public void MovementPayload_RejectsRotationOutsidePlausibleRange()
    {
        var writer = new TestBitWriter();
        writer.Skip(171);
        writer.WriteSingle(190.363f);
        writer.WriteQuantizedVector(0, 0, 0, 10);
        writer.WriteQuantizedVector(333668.77, -412585.53, 41068.87, 100);
        writer.WriteCompressedRotator(0, 123.75, 200); // 200 degrees of roll never happens on a pawn
        var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());

        Assert.False(NpcapGamePacketDecoder.TryReadMovementAt(bunch, 171, out _));
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

    [Fact]
    public void CandidateScan_PrefersLayoutWhoseGameClockAdvances()
    {
        var decoder = new NpcapGamePacketDecoder();
        var startedAt = DateTimeOffset.UtcNow;
        NpcapPositionSample? accepted = null;

        for (var frame = 0; frame < 4; frame++)
        {
            var writer = new TestBitWriter();
            // A decoy movement sits at offset 0 with a frozen timestamp, which is
            // exactly how a wrong bit offset looks in a real capture. The old scan
            // locked it because repeated timestamps counted as "standing still".
            WriteMovement(writer, 100f, -200_000, 300_000, 10_000, 180);
            writer.Skip(171 - writer.Position);
            WriteMovement(writer, 200f + frame * 0.2f, 120_000, -150_000, 30_000, 90);
            var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());

            if (decoder.TryDecodeMovement(bunch, startedAt.AddMilliseconds(frame * 200), out var sample))
            {
                accepted = sample;
            }
        }

        Assert.NotNull(accepted);
        Assert.Equal(120_000, accepted!.Value.Location.X, precision: 0);
        Assert.Equal(-150_000, accepted.Value.Location.Y, precision: 0);
        Assert.Equal(90d, accepted.Value.WorldYawDegrees!.Value, precision: 2);
    }

    [Fact]
    public void CandidateScan_IgnoresLayoutWithFrozenGameClock()
    {
        var decoder = new NpcapGamePacketDecoder();
        var startedAt = DateTimeOffset.UtcNow;

        for (var frame = 0; frame < 6; frame++)
        {
            var writer = new TestBitWriter();
            writer.Skip(171);
            WriteMovement(writer, 100f, -200_000, 300_000, 10_000, 180);
            var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());

            Assert.False(decoder.TryDecodeMovement(bunch, startedAt.AddMilliseconds(frame * 200), out _));
        }
    }

    [Fact]
    public void LockedLayout_RelocksAfterTheRpcLayoutMoves()
    {
        var decoder = new NpcapGamePacketDecoder();
        var startedAt = DateTimeOffset.UtcNow;
        var accepted = 0;

        for (var frame = 0; frame < 4; frame++)
        {
            var writer = new TestBitWriter();
            writer.Skip(171);
            WriteMovement(writer, 100f + frame * 0.2f, -200_000, 300_000, 10_000, 180);
            var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());
            if (decoder.TryDecodeMovement(bunch, startedAt.AddMilliseconds(frame * 200), out _)) accepted++;
        }

        Assert.Equal(2, accepted);

        // The server moved the move payload eight bits later; the decoder must not
        // stay pinned to the dead offset for the rest of the session.
        var resumedAt = startedAt.AddSeconds(4.2);
        NpcapPositionSample? relocked = null;
        for (var frame = 0; frame < 4; frame++)
        {
            var writer = new TestBitWriter();
            writer.Skip(179);
            WriteMovement(writer, 104.3f + frame * 0.2f, -200_100, 300_100, 10_000, 180);
            var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());
            if (decoder.TryDecodeMovement(bunch, resumedAt.AddMilliseconds(frame * 200), out var sample))
            {
                relocked = sample;
            }
        }

        Assert.NotNull(relocked);
        Assert.Equal(-200_100, relocked!.Value.Location.X, precision: 0);
    }

    [Fact]
    public void StandingPlayer_KeepsReportingWhileGameClockAdvances()
    {
        var decoder = new NpcapGamePacketDecoder();
        var startedAt = DateTimeOffset.UtcNow;
        var accepted = 0;
        NpcapPositionSample? last = null;

        for (var frame = 0; frame < 6; frame++)
        {
            var writer = new TestBitWriter();
            writer.Skip(171);
            WriteMovement(writer, 100f + frame * 0.2f, -200_000, 300_000, 10_000, 180);
            var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());
            if (decoder.TryDecodeMovement(bunch, startedAt.AddMilliseconds(frame * 200), out var sample))
            {
                accepted++;
                last = sample;
            }
        }

        Assert.Equal(4, accepted);
        Assert.Equal(-200_000, last!.Value.Location.X, precision: 0);
    }

    [Fact]
    public void SeedLocation_AnchorsDecoderToPlayerRegion()
    {
        var decoder = new NpcapGamePacketDecoder();
        var localLocation = new TheIsleOverlay.Core.WorldLocation { X = 348_816, Y = -203_171, Z = 22_484 };

        decoder.SeedLocation(localLocation);

        var writer = new TestBitWriter();
        writer.Skip(171);
        writer.WriteSingle(100f);
        writer.WriteQuantizedVector(0, 0, 0, 10);
        writer.WriteQuantizedVector(-200_000, 300_000, 10_000, 100);
        writer.WriteCompressedRotator(0, 0, 0);
        var bunch = new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray());

        var read = NpcapGamePacketDecoder.TryReadMovementAt(bunch, 171, out var movement);

        Assert.True(read);
        Assert.Equal(-200_000, movement.Location.X, precision: 0);
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

    private static void WriteMovement(
        TestBitWriter writer,
        float timestamp,
        double x,
        double y,
        double z,
        double yaw)
    {
        writer.WriteSingle(timestamp);
        writer.WriteQuantizedVector(0, 0, 0, 10);
        writer.WriteQuantizedVector(x, y, z, 100);
        writer.WriteCompressedRotator(0, yaw, 0);
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
            // FRotator::SerializeCompressedShort sits directly behind the location
            // vector: one presence bit per axis followed by its 16-bit value.
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
