using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

/// <summary>
/// The observed SBTC Iris attribute-set schema. Change-mask bytes are sparse:
/// a four-bit byte selector precedes the selected bytes of the 32-bit mask.
/// Attribute values are presence-bit floats (base/current), not fixed offsets.
/// Unsupported schema headers are deliberately ignored.
/// </summary>
internal static class NpcapDinosaurVitalDecoder
{
    internal const uint AttributeSubobjectOffset = 6;
    private const uint SupportedHeader = 0x28;
    // The observed schema's unmodified thirst capacity is 1000 (also confirmed
    // by the player's game readouts). Iris omits unchanged class defaults.
    // An explicit replicated capacity always overrides this schema default.
    private const double DefaultMaxThirst = 1000d;
    private const uint RelevantMask = (1u << 0) | (1u << 1) | (1u << 2) | (1u << 3) |
        (1u << 21) | (1u << 22) | (1u << 23) | (1u << 24);

    internal static bool TryReadMovementHandle(byte[] data, int bitLength, int movementOffset, out uint handle)
    {
        handle = 0;
        if (bitLength < 0 || bitLength > data.Length * 8 || movementOffset < 0 || movementOffset > bitLength) return false;
        // Two captured RPC sizes: the packed movement length occupies one or
        // two bytes. Require both the RPC suffix and the already proven movement
        // layout; a random integer in an outbound packet is never ownership.
        foreach (var suffixBits in new[] { 34, 42 })
        {
            var end = movementOffset - suffixBits;
            if (end < 11 || end + 22 > bitLength || Read(data, end, 22) != 0x20008) continue;
            for (var bytes = 1; bytes <= 4; bytes++)
            {
                var start = end - bytes * 8;
                if (start < 20 || Read(data, start - 20, 17) != 0x10000 ||
                    Read(data, start - 3, 3) != bytes - 1) continue;
                var value = Read(data, start, bytes * 8);
                if (value == 0 || (value & 1) != 0 || bytes > 1 && value < (1u << ((bytes - 1) * 8))) continue;
                if (handle != 0 && handle != value) return false;
                handle = value;
            }
        }
        return handle != 0;
    }

    internal static IReadOnlyList<NpcapAttributeUpdate> ReadUpdates(byte[] data, int bitLength)
    {
        var result = new List<NpcapAttributeUpdate>();
        if (bitLength < 0 || bitLength > data.Length * 8) return result;
        for (var prefix = 0; prefix + 41 <= bitLength; prefix++)
        {
            var bytes = (int)Read(data, prefix, 3) + 1;
            if (bytes > 4) continue;
            var header = prefix + 3 + bytes * 8;
            if (header + 14 > bitLength || Read(data, header, 10) != SupportedHeader) continue;
            var handle = Read(data, prefix + 3, bytes * 8);
            if (handle <= AttributeSubobjectOffset || (handle & 1) != 0 ||
                bytes > 1 && handle < (1u << ((bytes - 1) * 8))) continue;
            var selector = Read(data, header + 10, 4);
            var at = header + 14;
            uint mask = 0;
            var valid = true;
            for (var index = 0; index < 4; index++)
            {
                if ((selector & (1u << index)) == 0) continue;
                if (at + 8 > bitLength) { valid = false; break; }
                var value = Read(data, at, 8);
                if (value == 0) { valid = false; break; }
                mask |= value << (index * 8);
                at += 8;
            }
            if (!valid || (mask & RelevantMask) == 0) continue;
            var values = new double?[32];
            for (var field = 0; field < 32 && valid; field++)
            {
                if ((mask & (1u << field)) == 0) continue;
                // Consume unknown attributes too: they change subsequent offsets.
                for (var component = 0; component < 2; component++)
                {
                    if (at >= bitLength) { valid = false; break; }
                    var present = Read(data, at++, 1) != 0;
                    double value = 0;
                    if (present)
                    {
                        if (at + 32 > bitLength) { valid = false; break; }
                        value = BitConverter.UInt32BitsToSingle(Read(data, at, 32));
                        at += 32;
                    }
                    if (!double.IsFinite(value) || value < 0 || value > 100_000_000d) { valid = false; break; }
                    if (component == 1) values[field] = value;
                }
            }
            if (!valid) continue;
            var vitals = new ExactVitals
            {
                Hunger = values[0], MaxHunger = Positive(values[1]),
                Thirst = values[2], MaxThirst = Positive(values[3]),
                Health = values[21], MaxHealth = Positive(values[22]),
                Stamina = values[23], MaxStamina = Positive(values[24])
            };
            result.Add(new(handle, prefix, vitals));
            prefix = at - 1;
        }
        return result;
    }

    internal static ExactVitals Merge(ExactVitals old, ExactVitals next) => old with
    {
        Health = next.Health ?? old.Health, MaxHealth = next.MaxHealth ?? old.MaxHealth,
        Hunger = next.Hunger ?? old.Hunger, MaxHunger = next.MaxHunger ?? old.MaxHunger,
        Thirst = next.Thirst ?? old.Thirst,
        MaxThirst = next.MaxThirst ?? old.MaxThirst ?? (next.Thirst is not null ? DefaultMaxThirst : null),
        Stamina = next.Stamina ?? old.Stamina, MaxStamina = next.MaxStamina ?? old.MaxStamina
    };

    private static double? Positive(double? value) => value is > 0 ? value : null;
    private static uint Read(byte[] data, int offset, int count)
    {
        uint result = 0;
        for (var bit = 0; bit < count; bit++)
            result |= (uint)((data[(offset + bit) >> 3] >> ((offset + bit) & 7)) & 1) << bit;
        return result;
    }
}

internal readonly record struct NpcapAttributeUpdate(uint Handle, int BitOffset, ExactVitals Vitals);
internal sealed record NpcapVitalSample(uint ActorHandle, DateTimeOffset CapturedAt, ExactVitals Vitals);
