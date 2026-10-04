namespace TheIsleOverlay.App.Tests;

/// <summary>LSB-first bit writer matching Unreal's bunch serialization.</summary>
internal sealed class TestBitWriter
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

    public void WriteBit(bool value) => _bits.Add(value);

    public void WriteUInt32Packed(uint value)
    {
        do
        {
            var next = value >> 7;
            WriteBytes([(byte)(((value & 127) << 1) | (next != 0 ? 1u : 0u))]);
            value = next;
        } while (value != 0);
    }

    public void WriteBits(byte[] bytes, int offset, int count)
    {
        for (var bit = offset; bit < offset + count; bit++)
            _bits.Add(((bytes[bit >> 3] >> (bit & 7)) & 1) != 0);
    }

    public void WriteBytes(byte[] bytes)
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

    public void WriteSerializedInt(int value, int valueMax)
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
