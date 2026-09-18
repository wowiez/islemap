using System.Buffers.Binary;
using System.IO;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal sealed class NpcapGamePacketDecoder
{
    private const int StatelessPrefixBits = 6;
    private const int MaximumChannels = 16384;
    private const int MaximumBunchBits = 8192;
    private static readonly TimeSpan CandidateExpiry = TimeSpan.FromSeconds(2);
    private readonly Dictionary<(uint Channel, int Offset), MovementCandidate> _candidates = [];
    private (uint Channel, int Offset)? _lockedCandidate;
    private DateTimeOffset _lastLockedAt;

    public bool TryProcessEthernetFrame(
        ReadOnlySpan<byte> frame,
        DateTimeOffset capturedAt,
        out NpcapPositionSample sample)
    {
        sample = default;
        return TryReadUdpPayload(frame, out var payload) &&
               TryProcessGamePayload(payload, capturedAt, out sample);
    }

    internal bool TryProcessGamePayload(
        ReadOnlySpan<byte> payload,
        DateTimeOffset capturedAt,
        out NpcapPositionSample sample)
    {
        sample = default;
        // The first packet-handler byte is session-dependent (observed 0x0c,
        // 0x10 and 0x14). Its low two flag bits stay clear for these game
        // packets; the full Unreal header and repeated movement validation
        // below remain the authoritative false-positive guard.
        if (payload.Length < 10 || !IsSupportedPacketPrefix(payload[0]))
        {
            return false;
        }

        // The packet-handler prefix is one byte on older servers and two bytes
        // on the current SBTC build. Probe both layouts; movement still has to
        // pass the multi-frame timestamp/location validation below.
        return TryProcessPacketLayout(payload, capturedAt, 8, out sample) ||
               TryProcessPacketLayout(payload, capturedAt, 16, out sample);
    }

    internal static bool IsSupportedPacketPrefix(byte value) => (value & 0x03) == 0;

    private bool TryProcessPacketLayout(
        ReadOnlySpan<byte> payload,
        DateTimeOffset capturedAt,
        int packetPrefixBits,
        out NpcapPositionSample sample)
    {
        sample = default;
        try
        {
            var outerEnd = FindTrailingOne(payload, payload.Length * 8);
            var innerEnd = outerEnd < 0 ? -1 : FindTrailingOne(payload, outerEnd);
            if (innerEnd <= packetPrefixBits + StatelessPrefixBits + 64)
            {
                return false;
            }

            var reader = new UnrealBitReader(payload.ToArray(), innerEnd);
            reader.Skip(packetPrefixBits);
            reader.ReadSerializedInt(4);
            reader.ReadSerializedInt(8);
            if (reader.ReadBit())
            {
                return false;
            }

            var packedHeader = reader.ReadUInt32();
            var historyWords = (int)(packedHeader & 0x0f) + 1;
            if (historyWords is < 1 or > 8)
            {
                return false;
            }

            reader.Skip(historyWords * 32);
            if (reader.ReadBit())
            {
                reader.ReadSerializedInt(1024);
                if (reader.ReadBit()) reader.Skip(8);
            }

            while (reader.Remaining >= 10)
            {
                if (!TryReadBunch(reader, out var bunch))
                {
                    return false;
                }

                if (TryDecodeMovement(bunch, capturedAt, out sample))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or OverflowException)
        {
            return false;
        }
    }

    private bool TryDecodeMovement(
        ParsedBunch bunch,
        DateTimeOffset capturedAt,
        out NpcapPositionSample sample)
    {
        sample = default;
        if (_lockedCandidate is { } locked && capturedAt - _lastLockedAt <= CandidateExpiry)
        {
            if (locked.Channel == bunch.Channel &&
                TryReadMovementAt(bunch, locked.Offset, out var lockedMove) &&
                AcceptCandidate(locked, lockedMove, capturedAt, out sample, lockAfter: 1))
            {
                _lastLockedAt = capturedAt;
                return true;
            }

            // Other RPCs share this actor channel. Keep the proven movement
            // layout locked instead of allowing their bytes to create a false
            // candidate while a real movement frame is briefly absent.
            return false;
        }

        var maximumOffset = Math.Min(384, bunch.PayloadBits - 120);
        for (var offset = 0; offset <= maximumOffset; offset++)
        {
            if (!TryReadMovementAt(bunch, offset, out var move)) continue;
            var key = (bunch.Channel, offset);
            if (AcceptCandidate(key, move, capturedAt, out sample, lockAfter: 3))
            {
                _lockedCandidate = key;
                _lastLockedAt = capturedAt;
                return true;
            }
        }

        return false;
    }

    private bool AcceptCandidate(
        (uint Channel, int Offset) key,
        DecodedMovement move,
        DateTimeOffset capturedAt,
        out NpcapPositionSample sample,
        int lockAfter)
    {
        sample = default;
        if (!_candidates.TryGetValue(key, out var previous) ||
            capturedAt - previous.CapturedAt > CandidateExpiry)
        {
            _candidates[key] = new MovementCandidate(move.Timestamp, move.Location, capturedAt, 1);
            return false;
        }

        var wallDelta = (capturedAt - previous.CapturedAt).TotalSeconds;
        var timestampReset = previous.Timestamp > 200 && move.Timestamp is >= 0 and < 5;
        var gameDelta = timestampReset
            ? move.Timestamp + 240f - previous.Timestamp
            : move.Timestamp - previous.Timestamp;
        if (gameDelta <= 0 || gameDelta > 1.5f)
        {
            _candidates[key] = new MovementCandidate(move.Timestamp, move.Location, capturedAt, 1);
            return false;
        }
        var planarDistance = Math.Sqrt(
            Math.Pow(move.Location.X - previous.Location.X, 2) +
            Math.Pow(move.Location.Y - previous.Location.Y, 2));
        var plausible = wallDelta is > 0 and <= 2 &&
                        Math.Abs(gameDelta - wallDelta) <= Math.Max(0.25, wallDelta * 1.5) &&
                        planarDistance <= Math.Max(50_000, wallDelta * 25_000);
        var streak = plausible ? previous.Streak + 1 : 1;
        _candidates[key] = new MovementCandidate(move.Timestamp, move.Location, capturedAt, streak);
        if (streak < lockAfter)
        {
            return false;
        }

        sample = new NpcapPositionSample(
            move.Location,
            capturedAt,
            move.Timestamp,
            move.ControlYawDegrees);
        return true;
    }

    internal static bool TryReadMovementAt(
        ParsedBunch bunch,
        int bitOffset,
        out DecodedMovement movement)
    {
        movement = default;
        try
        {
            var reader = new UnrealBitReader(bunch.Payload, bunch.PayloadBits);
            reader.Skip(bitOffset);
            var timestamp = reader.ReadSingle();
            var acceleration = reader.ReadQuantizedVector(10);
            var location = reader.ReadQuantizedVector(100);
            // FRotator::SerializeCompressedShort writes a presence bit followed
            // by a 16-bit compressed value for each Pitch/Yaw/Roll axis.
            _ = reader.ReadCompressedRotationAxisShort(); // Pitch is not used by the 2D map.
            var controlYaw = reader.ReadCompressedRotationAxisShort();
            _ = reader.ReadCompressedRotationAxisShort(); // Roll is not used by the 2D map.
            if (!float.IsFinite(timestamp) || timestamp is < 1 or > 1_000_000 ||
                !IsPlausibleVector(acceleration, 100_000, 100_000) ||
                !IsPlausibleVector(location, 1_000_000, 500_000) ||
                Math.Abs(location.X) + Math.Abs(location.Y) < 1_000)
            {
                return false;
            }

            movement = new DecodedMovement(timestamp, location, controlYaw);
            return true;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or OverflowException)
        {
            return false;
        }
    }

    private static bool IsPlausibleVector(WorldLocation value, double xyLimit, double zLimit) =>
        double.IsFinite(value.X) && double.IsFinite(value.Y) &&
        value.Z is { } z && double.IsFinite(z) &&
        Math.Abs(value.X) <= xyLimit && Math.Abs(value.Y) <= xyLimit && Math.Abs(z) <= zLimit;

    private static bool TryReadBunch(UnrealBitReader reader, out ParsedBunch bunch)
    {
        bunch = default;
        var control = reader.ReadBit();
        var open = control && reader.ReadBit();
        var close = control && reader.ReadBit();
        if (close) reader.ReadSerializedInt(15);
        reader.ReadBit();
        var reliable = reader.ReadBit();
        var channel = reader.ReadUInt32Packed();
        if (channel >= MaximumChannels) return false;
        reader.ReadBit();
        reader.ReadBit();
        var partial = reader.ReadBit();
        if (reliable) reader.ReadSerializedInt(1024);
        if (partial)
        {
            reader.ReadBit();
            reader.ReadBit();
            reader.ReadBit();
        }
        if (open || reliable)
        {
            if (!reader.ReadBit()) return false;
            reader.ReadUInt32Packed();
        }

        var payloadBits = reader.ReadSerializedInt(MaximumBunchBits);
        if (payloadBits > reader.Remaining) return false;
        bunch = new ParsedBunch(channel, payloadBits, reader.ReadBits(payloadBits));
        return true;
    }

    private static int FindTrailingOne(ReadOnlySpan<byte> bytes, int beforeBit)
    {
        for (var bit = beforeBit - 1; bit >= 0; bit--)
        {
            if (((bytes[bit >> 3] >> (bit & 7)) & 1) != 0) return bit;
        }
        return -1;
    }

    private static bool TryReadUdpPayload(ReadOnlySpan<byte> frame, out ReadOnlySpan<byte> payload)
    {
        payload = default;
        if (frame.Length < 42) return false;
        var etherType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));
        var ipOffset = etherType == 0x8100 ? 18 : 14;
        if (frame.Length < ipOffset + 28 || frame[ipOffset] >> 4 != 4 || frame[ipOffset + 9] != 17) return false;
        var ipHeaderLength = (frame[ipOffset] & 0x0f) * 4;
        var udpOffset = ipOffset + ipHeaderLength;
        if (frame.Length < udpOffset + 8) return false;
        var udpLength = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(udpOffset + 4, 2));
        var payloadLength = Math.Max(0, Math.Min(udpLength - 8, frame.Length - udpOffset - 8));
        payload = frame.Slice(udpOffset + 8, payloadLength);
        return true;
    }

    internal readonly record struct ParsedBunch(uint Channel, int PayloadBits, byte[] Payload);
    internal readonly record struct DecodedMovement(
        float Timestamp,
        WorldLocation Location,
        double ControlYawDegrees);
    private readonly record struct MovementCandidate(float Timestamp, WorldLocation Location, DateTimeOffset CapturedAt, int Streak);

    private sealed class UnrealBitReader(byte[] bytes, int limit)
    {
        public int Position { get; private set; }
        public int Remaining => limit - Position;

        public bool ReadBit()
        {
            if (Position >= limit) throw new EndOfStreamException();
            return ((bytes[Position >> 3] >> (Position++ & 7)) & 1) != 0;
        }

        public int ReadSerializedInt(int valueMax)
        {
            if (valueMax < 2) throw new ArgumentOutOfRangeException(nameof(valueMax));
            var value = 0;
            var mask = 1;
            while (value + mask < valueMax)
            {
                if (ReadBit()) value |= mask;
                mask <<= 1;
            }
            return value;
        }

        public uint ReadUInt32()
        {
            uint value = 0;
            for (var bit = 0; bit < 32; bit++) if (ReadBit()) value |= 1u << bit;
            return value;
        }

        public ushort ReadUInt16()
        {
            ushort value = 0;
            for (var bit = 0; bit < 16; bit++) if (ReadBit()) value |= (ushort)(1 << bit);
            return value;
        }

        public double ReadCompressedRotationAxisShort() =>
            ReadBit() ? ReadUInt16() * 360d / 65536d : 0d;

        public uint ReadUInt32Packed()
        {
            uint value = 0;
            for (var shift = 0; shift < 35; shift += 7)
            {
                uint encoded = 0;
                for (var bit = 0; bit < 8; bit++) if (ReadBit()) encoded |= 1u << bit;
                value |= (encoded >> 1) << shift;
                if ((encoded & 1) == 0) return value;
            }
            throw new InvalidDataException("Packed integer overflow.");
        }

        public float ReadSingle() => BitConverter.ToSingle(ReadBits(32));

        public WorldLocation ReadQuantizedVector(int scale)
        {
            var header = ReadSerializedInt(128);
            var componentBits = header & 0x3f;
            var scaled = (header & 0x40) != 0;
            if (componentBits == 0)
            {
                return scaled
                    ? new WorldLocation { X = BitConverter.ToDouble(ReadBits(64)), Y = BitConverter.ToDouble(ReadBits(64)), Z = BitConverter.ToDouble(ReadBits(64)) }
                    : new WorldLocation { X = ReadSingle(), Y = ReadSingle(), Z = ReadSingle() };
            }
            if (componentBits > 63) throw new InvalidDataException("Invalid quantized vector width.");
            var divisor = scaled ? scale : 1;
            return new WorldLocation
            {
                X = (double)ReadSigned(componentBits) / divisor,
                Y = (double)ReadSigned(componentBits) / divisor,
                Z = (double)ReadSigned(componentBits) / divisor
            };
        }

        public byte[] ReadBits(int bitCount)
        {
            if (bitCount < 0 || bitCount > Remaining) throw new EndOfStreamException();
            var result = new byte[(bitCount + 7) / 8];
            for (var bit = 0; bit < bitCount; bit++) if (ReadBit()) result[bit >> 3] |= (byte)(1 << (bit & 7));
            return result;
        }

        public void Skip(int bitCount)
        {
            if (bitCount < 0 || bitCount > Remaining) throw new EndOfStreamException();
            Position += bitCount;
        }

        private long ReadSigned(int bitCount)
        {
            ulong raw = 0;
            for (var bit = 0; bit < bitCount; bit++) if (ReadBit()) raw |= 1UL << bit;
            if ((raw & (1UL << (bitCount - 1))) != 0) raw |= ulong.MaxValue << bitCount;
            return unchecked((long)raw);
        }
    }
}

internal readonly record struct NpcapPositionSample(
    WorldLocation Location,
    DateTimeOffset CapturedAt,
    float GameTimestamp,
    double? WorldYawDegrees = null);
