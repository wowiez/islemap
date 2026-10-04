using System.Buffers.Binary;

namespace TheIsleOverlay.App;

/// <summary>
/// Reads the mass field in a dinosaur's replicated attribute block. Each present
/// scalar is a one-bit presence flag followed by a 32-bit float. The block repeats
/// the mass in its capacity fields, including a half-mass capacity. Those exact
/// relationships identify the field even when preceding optional fields move it.
/// The preceding non-capacity attribute pair is required as context: capacity
/// copies alone also occur in unrelated data on the same actor channel.
/// Current health may differ from maximum. Small dinosaurs can weigh under 5 kg.
/// A lone float, a continuity chain or a value close to the HUD is not evidence.
/// </summary>
internal static class NpcapDinosaurWeightDecoder
{
    private const int ScalarBits = 33;
    private const int AttributeScalars = 20;
    private const int ShortAttributeScalars = 16;
    private const int MassIndex = 4;
    internal const int MinimumPayloadBits = ShortAttributeScalars * ScalarBits;
    // Only unowned, early replication expires. Once the client's actor is known,
    // unchanged mass may stop replicating entirely while the connection stays live.
    internal static readonly TimeSpan PendingSampleLifetime = TimeSpan.FromSeconds(120);

    public static bool TryDecode(
        ReadOnlySpan<byte> payload,
        int payloadBits,
        out double kilograms,
        out int bitOffset)
    {
        kilograms = 0;
        bitOffset = 0;
        if (payloadBits < MinimumPayloadBits || payloadBits > payload.Length * 8)
        {
            return false;
        }

        var found = false;
        var lastOffset = payloadBits - MinimumPayloadBits + 1;
        Span<float> fields = stackalloc float[AttributeScalars];
        for (var offset = 1; offset <= lastOffset; offset++)
        {
            if (!TryReadScalar(payload, offset, out var context) || context <= 0f ||
                !TryReadScalar(payload, offset + ScalarBits, out var repeatedContext) || context != repeatedContext)
            {
                continue;
            }
            var massOffset = offset + MassIndex * ScalarBits;
            if (!TryReadScalar(payload, massOffset, out var mass) || mass is <= 0f or > 20_000f)
            {
                continue;
            }
            // This attribute is not a percentage: live Ptera updates at 83%
            // growth contain 1.1345..1.1373 here. Require its repeated context,
            // but never impose a growth/percentage ceiling on it. Full/half
            // capacity copies cannot substitute for that context.
            if (context == mass || context == mass * 0.5f) continue;

            if (!MatchesBlock(payload, payloadBits, offset, mass, AttributeScalars, fields) &&
                !MatchesBlock(payload, payloadBits, offset, mass, ShortAttributeScalars, fields))
            {
                continue;
            }

            // Multiple different attribute blocks cannot be attributed to one dinosaur.
            if (found && kilograms != mass)
            {
                kilograms = 0;
                bitOffset = 0;
                return false;
            }

            found = true;
            kilograms = mass;
            bitOffset = massOffset;
        }

        return found;
    }

    private static bool MatchesBlock(ReadOnlySpan<byte> payload, int payloadBits, int offset,
        float mass, int scalarCount, Span<float> fields)
    {
        if (offset - 1 + scalarCount * ScalarBits > payloadBits) return false;
        for (var index = 0; index < scalarCount; index++)
            if (!TryReadScalar(payload, offset + index * ScalarBits, out fields[index])) return false;

        if (fields[2] != fields[3] || fields[2] > mass || fields[5] != mass) return false;
        // The captured Ptera layout omits the two intermediate attribute pairs.
        // Both layouts still require the same context, health pair, four half-mass
        // copies and six full-mass copies; a capacity tail alone is insufficient.
        var capacity = scalarCount == AttributeScalars ? 10 : 6;
        if (capacity == 10 && (fields[6] != fields[7] || fields[8] != fields[9])) return false;
        for (var index = capacity; index < capacity + 2; index++)
            if (fields[index] != mass) return false;
        for (var index = capacity + 2; index < capacity + 6; index++)
            if (fields[index] != mass * 0.5f) return false;
        for (var index = capacity + 6; index < scalarCount; index++)
            if (fields[index] != mass) return false;
        return true;
    }

    private static bool TryReadScalar(ReadOnlySpan<byte> payload, int offset, out float value)
    {
        value = 0;
        var flag = offset - 1;
        if (((payload[flag >> 3] >> (flag & 7)) & 1) == 0)
        {
            return false;
        }

        var firstByte = offset >> 3;
        var shift = offset & 7;
        var raw = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(firstByte, 4));
        if (shift != 0)
        {
            raw = (raw >> shift) | ((uint)payload[firstByte + 4] << (32 - shift));
        }

        value = BitConverter.UInt32BitsToSingle(raw);
        return float.IsFinite(value) && value is >= 0f and <= 20_000f;
    }
}

internal readonly record struct NpcapWeightSample(
    double Kilograms,
    DateTimeOffset CapturedAt,
    uint Channel,
    int BitOffset)
{
    public bool ActorConfirmed { get; init; }
}
