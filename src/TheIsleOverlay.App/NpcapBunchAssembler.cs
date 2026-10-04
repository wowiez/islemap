namespace TheIsleOverlay.App;

/// <summary>
/// Reassembles reliable actor bunches before looking for movement or attributes.
/// Fragments must be contiguous on the same channel and arrive within five seconds.
/// Lost, conflicting or unsupported fragments are discarded rather than scanned as
/// complete attributes. No packets from different connections share an assembler.
/// </summary>
internal sealed class NpcapBunchAssembler
{
    private const int MaximumChannels = 64;
    private const int MaximumBits = 512 * 1024;
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);
    private readonly Dictionary<uint, PartialBunch> _partials = [];

    public void Clear() => _partials.Clear();

    public bool TryAssemble(NpcapGamePacketDecoder.ParsedBunch fragment, DateTimeOffset now,
        out NpcapGamePacketDecoder.ParsedBunch bunch)
    {
        bunch = default;
        foreach (var expired in _partials.Where(entry => now - entry.Value.StartedAt > Lifetime)
            .Select(entry => entry.Key).ToArray()) _partials.Remove(expired);

        if (!fragment.Partial)
        {
            _partials.Remove(fragment.Channel);
            if (fragment.Close) return false;
            bunch = fragment;
            return true;
        }

        // Export fragments have their own serialization and aren't raw actor data.
        // Unreliable fragmentation needs packet-sequence recovery; don't guess it.
        if (!fragment.Reliable || fragment.Close || fragment.HasPackageMapExports ||
            fragment.PartialCustomExportsFinal || fragment.PayloadBits <= 0 ||
            fragment.PayloadBits > fragment.Payload.Length * 8 ||
            (!fragment.PartialFinal && fragment.PayloadBits % 8 != 0))
        {
            _partials.Remove(fragment.Channel);
            return false;
        }

        if (_partials.TryGetValue(fragment.Channel, out var pending) && fragment.Sequence == pending.Sequence)
        {
            if (fragment.PayloadBits != pending.LastBits ||
                !fragment.Payload.AsSpan().SequenceEqual(pending.LastPayload)) _partials.Remove(fragment.Channel);
            return false; // exact retransmission, or conflicting sequence
        }

        if (fragment.PartialInitial)
        {
            if (_partials.Count >= MaximumChannels && !_partials.ContainsKey(fragment.Channel))
                _partials.Remove(_partials.MinBy(entry => entry.Value.StartedAt).Key);
            pending = new PartialBunch(now);
            _partials[fragment.Channel] = pending;
        }
        else if (pending is null || fragment.Sequence != (pending.Sequence + 1) % 1024 ||
                 now < pending.LastAt)
        {
            _partials.Remove(fragment.Channel);
            return false;
        }

        if (pending.Bits + fragment.PayloadBits > MaximumBits)
        {
            _partials.Remove(fragment.Channel);
            return false;
        }
        // Every preceding fragment is byte-aligned, so only the final byte needs
        // its exact bit count retained; padding is never scanned as payload.
        pending.Bytes.AddRange(fragment.Payload.AsSpan(0, (fragment.PayloadBits + 7) / 8).ToArray());
        pending.Bits += fragment.PayloadBits;
        pending.Sequence = fragment.Sequence;
        pending.LastBits = fragment.PayloadBits;
        pending.LastPayload = fragment.Payload;
        pending.LastAt = now;
        if (!fragment.PartialFinal) return false;

        _partials.Remove(fragment.Channel);
        bunch = new(fragment.Channel, pending.Bits, pending.Bytes.ToArray());
        return true;
    }

    private sealed class PartialBunch(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; } = startedAt;
        public DateTimeOffset LastAt { get; set; } = startedAt;
        public List<byte> Bytes { get; } = [];
        public int Bits { get; set; }
        public int Sequence { get; set; } = -1;
        public int LastBits { get; set; }
        public byte[] LastPayload { get; set; } = [];
    }
}
