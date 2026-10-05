using System.Buffers.Binary;
using System.IO;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

internal sealed class NpcapGamePacketDecoder
{
    private const int StatelessPrefixBits = 6;

    // Synthetic channel for movement found straight in the datagram.
    private const uint MovementChannel = 0xFFFF_FFFF;
    private const int MaximumChannels = 16384;
    private const int MaximumBunchBits = 8192;

    private const int RequiredAdvancingFrames = 2;
    private const float MinimumTimestampAdvance = 0.02f;
    private const double MaximumFrameSeconds = 2.0d;
    private static readonly TimeSpan CandidateExpiry = TimeSpan.FromSeconds(15);

    // A locked layout keeps serving frames until it stays silent for this long.
    // Without the deadline a session whose RPC layout shifted would keep the old
    // offset locked and never look for the live one again.
    private static readonly TimeSpan LockedCandidateTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan AnchorHoldDuration = TimeSpan.FromSeconds(15);
    private readonly Dictionary<(uint Channel, int Offset), MovementCandidate> _candidates = [];
    private readonly Dictionary<uint, NpcapWeightSample> _pendingWeights = [];
    private readonly NpcapBunchAssembler _bunchAssembler = new();
    private NpcapWeightSample? _weightSample;
    private (uint Channel, int Offset)? _lockedCandidate;
    private uint? _statisticsChannel;
    private DateTimeOffset _lastLockedAt;
    private WorldLocation? _verifiedPlayerAnchor;
    private DateTimeOffset _anchorAt;
    private readonly Dictionary<(uint Channel, uint Handle), NpcapVitalSample> _pendingVitals = [];
    private NpcapVitalSample? _vitalSample;
    private uint? _statisticsActorHandle;
    private uint? _movementActorHandle;
    private uint? _movementActorChannel;
    private uint? _candidateActorHandle;
    private int _actorEvidence;
    private DateTimeOffset _actorEvidenceAt;
    private bool _irisStatistics;

    internal uint? MovementActorHandle => _movementActorHandle;
    internal uint? StatisticsActorHandle
    {
        get => _statisticsActorHandle;
        set
        {
            if (_statisticsActorHandle == value) return;
            _statisticsActorHandle = value;
            if (value is not null) _irisStatistics = true;
            _vitalSample = null;
            _weightSample = null;
        }
    }

    internal NpcapVitalSample? VitalSample(DateTimeOffset now)
    {
        if (_statisticsChannel is not { } channel || _statisticsActorHandle is not { } actor) return null;
        if (_vitalSample is null && _pendingVitals.TryGetValue((channel, actor + NpcapDinosaurVitalDecoder.AttributeSubobjectOffset), out var pending) &&
            now >= pending.CapturedAt && now - pending.CapturedAt <= NpcapDinosaurWeightDecoder.PendingSampleLifetime)
            _vitalSample = pending;
        return _vitalSample is { } sample && now >= sample.CapturedAt ? sample : null;
    }

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
        if (TryReadPacketLayout(payload, 8, 2, out var bunches) ||
            TryReadPacketLayout(payload, 16, 2, out bunches) ||
            TryReadPacketLayout(payload, 8, 1, out bunches) ||
            TryReadPacketLayout(payload, 16, 1, out bunches))
        {
            var decoded = false;
            foreach (var fragment in bunches)
            {
                if (fragment.Close)
                {
                    _pendingWeights.Remove(fragment.Channel);
                    if (_weightSample is { } previous && previous.Channel == fragment.Channel) _weightSample = null;
                    foreach (var key in _pendingVitals.Keys.Where(key => key.Channel == fragment.Channel).ToArray()) _pendingVitals.Remove(key);
                    if (_statisticsChannel == fragment.Channel) _vitalSample = null;
                    if (_lockedCandidate?.Channel == fragment.Channel || _movementActorChannel == fragment.Channel)
                    {
                        _movementActorHandle = null;
                        _movementActorChannel = null;
                        _candidateActorHandle = null;
                        _actorEvidence = 0;
                    }
                }
                if (!_bunchAssembler.TryAssemble(fragment, capturedAt, out var bunch)) continue;
                if (!decoded && TryDecodeMovement(bunch, capturedAt, out var decodedSample))
                {
                    sample = decodedSample;
                    decoded = true;
                    ObserveMovementActor(bunch, capturedAt);
                }
                ObserveStatistics(bunch, capturedAt);
            }
            if (decoded) return true;
        }

        // A client build can change the bunch header without moving the movement RPC
        // itself: on a live capture the RPC still sits inside the datagram (fitted at
        // bit 288) while the header no longer parses. Probing the whole datagram as one
        // "bunch" keeps the position working, and the clipboard anchor plus the
        // multi-frame clock validation still keep other players' movement out.
        return TryDecodeMovement(
            new ParsedBunch(MovementChannel, payload.Length * 8, payload.ToArray()),
            capturedAt,
            out sample);
    }

    internal static bool IsSupportedPacketPrefix(byte value) => (value & 0x03) == 0;

    /// <summary>
    /// Validate the entire packet layout before committing any actor or fragment
    /// state. Packet handlers can add their own termination bit; some layouts only
    /// have the engine's termination bit. Stripping two bits unconditionally used to
    /// truncate those packets, leaving only the channel-less movement fallback.
    /// </summary>
    private static bool TryReadPacketLayout(
        ReadOnlySpan<byte> payload,
        int packetPrefixBits,
        int terminationBits,
        out List<ParsedBunch> bunches)
    {
        bunches = [];
        try
        {
            var outerEnd = FindTrailingOne(payload, payload.Length * 8);
            var innerEnd = terminationBits == 1 ? outerEnd : outerEnd < 0 ? -1 : FindTrailingOne(payload, outerEnd);
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
                    bunches.Clear();
                    return false;
                }
                bunches.Add(bunch);
            }
            // A successful prefix must account for every bit, not merely happen
            // to find one plausible bunch inside a different header layout.
            return bunches.Count > 0 && reader.Remaining == 0;
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or OverflowException)
        {
            return false;
        }
    }

    /// <summary>
    /// The actor channel the local player's movement RPC named, if any. Movement found
    /// straight in the datagram belongs to no actor channel, so it reports null.
    /// </summary>
    internal uint? LockedChannel
    {
        get
        {
            lock (_candidates)
            {
                return _lockedCandidate is { } locked && locked.Channel != MovementChannel
                    ? locked.Channel
                    : null;
            }
        }
    }

    internal bool HasLockedMovement => _lockedCandidate is not null;

    /// <summary>
    /// The actor channel whose bunches carry the local player's statistics. The server
    /// replicates every nearby actor on its own channel, so without this the mass scan
    /// would follow whichever dinosaur happened to be on the channel it guessed.
    /// </summary>
    internal uint? StatisticsChannel
    {
        get => _statisticsChannel;
        set
        {
            if (_statisticsChannel == value) return;
            _weightSample = _statisticsChannel is null && value is { } channel &&
                _pendingWeights.TryGetValue(channel, out var pending) ? pending : null;
            _pendingWeights.Clear();
            _vitalSample = null;
            if (_statisticsChannel is not null) _pendingVitals.Clear();
            if (_statisticsChannel is not null) _bunchAssembler.Clear();
            _statisticsChannel = value;
        }
    }

    internal NpcapWeightSample? WeightSample(DateTimeOffset now)
    {
        if (_weightSample is not { } sample || now < sample.CapturedAt) return null;
        if (!sample.ActorConfirmed)
        {
            // Do not revive old replication when ownership is identified late.
            if (now - sample.CapturedAt > NpcapDinosaurWeightDecoder.PendingSampleLifetime) return null;
            _weightSample = sample = sample with { ActorConfirmed = true };
        }
        // Selected-actor mass remains valid until its channel/connection is reset.
        // CapturedAt is kept intact: ordinary traffic is not a new KG measurement.
        return sample;
    }

    internal void ObserveStatistics(ParsedBunch bunch, DateTimeOffset capturedAt)
    {
        ObserveVitals(bunch, capturedAt);
        // Iris multiplexes nearby actors on the same channel. Once object
        // ownership is known, an unscoped legacy mass scan is unsafe.
        if (_irisStatistics) return;
        if ((_statisticsChannel is { } channel && channel != bunch.Channel) ||
            !NpcapDinosaurWeightDecoder.TryDecode(bunch.Payload, bunch.PayloadBits, out var kilograms, out var offset))
        {
            return;
        }

        var sample = new NpcapWeightSample(kilograms, capturedAt, bunch.Channel, offset)
        {
            ActorConfirmed = _statisticsChannel is not null
        };
        if (_statisticsChannel is null)
        {
            // Initial replication can precede the client's movement lock. Keep a
            // small, expiring numeric cache; never publish it until that same
            // connection's outbound RPC identifies the actor channel.
            foreach (var expired in _pendingWeights.Where(entry =>
                capturedAt - entry.Value.CapturedAt > NpcapDinosaurWeightDecoder.PendingSampleLifetime)
                .Select(entry => entry.Key).ToArray()) _pendingWeights.Remove(expired);
            if (_pendingWeights.TryGetValue(bunch.Channel, out var previous) && capturedAt < previous.CapturedAt) return;
            if (_pendingWeights.Count >= 64 && !_pendingWeights.ContainsKey(bunch.Channel))
                _pendingWeights.Remove(_pendingWeights.MinBy(entry => entry.Value.CapturedAt).Key);
            _pendingWeights[bunch.Channel] = sample;
        }
        else if (_weightSample is null || capturedAt >= _weightSample.Value.CapturedAt)
        {
            _weightSample = sample;
        }
    }

    private void ObserveMovementActor(ParsedBunch bunch, DateTimeOffset capturedAt)
    {
        if (_lockedCandidate is not { } locked || locked.Channel != bunch.Channel) return;
        if (!NpcapDinosaurVitalDecoder.TryReadMovementHandle(bunch.Payload, bunch.PayloadBits, locked.Offset, out var handle))
        {
            // An unreadable RPC is not evidence that the player changed actor.
            // Keep a confirmed identity until a close, reset or different handle.
            return;
        }
        if (capturedAt <= _actorEvidenceAt) return;
        if (_movementActorHandle == handle)
        {
            // Quiet movement gaps do not revoke a confirmed object identity.
            _movementActorChannel = bunch.Channel;
            _actorEvidenceAt = capturedAt;
            return;
        }
        if (_candidateActorHandle != handle || capturedAt - _actorEvidenceAt > TimeSpan.FromSeconds(3))
        {
            _candidateActorHandle = handle;
            _actorEvidence = 0;
            _movementActorHandle = null;
            _movementActorChannel = null;
        }
        _actorEvidenceAt = capturedAt;
        if (++_actorEvidence >= 2)
        {
            _movementActorHandle = handle;
            _movementActorChannel = bunch.Channel;
        }
    }

    private void ObserveVitals(ParsedBunch bunch, DateTimeOffset capturedAt)
    {
        if (_statisticsChannel is { } channel && channel != bunch.Channel) return;
        foreach (var expired in _pendingVitals.Where(pair => capturedAt - pair.Value.CapturedAt >
            NpcapDinosaurWeightDecoder.PendingSampleLifetime).Select(pair => pair.Key).ToArray()) _pendingVitals.Remove(expired);
        foreach (var update in NpcapDinosaurVitalDecoder.ReadUpdates(bunch.Payload, bunch.PayloadBits))
        {
            if (_statisticsActorHandle is { } actor && update.Handle != actor + NpcapDinosaurVitalDecoder.AttributeSubobjectOffset) continue;
            var key = (bunch.Channel, update.Handle);
            _pendingVitals.TryGetValue(key, out var previous);
            if (previous is not null && capturedAt < previous.CapturedAt) continue;
            var selected = _statisticsActorHandle is { } selectedActor &&
                selectedActor + NpcapDinosaurVitalDecoder.AttributeSubobjectOffset == update.Handle;
            var old = selected && _vitalSample is { } live ? live.Vitals : previous?.Vitals ?? new ExactVitals();
            var sample = new NpcapVitalSample(update.Handle - NpcapDinosaurVitalDecoder.AttributeSubobjectOffset,
                capturedAt, NpcapDinosaurVitalDecoder.Merge(old, update.Vitals));
            if (_pendingVitals.Count >= 64 && !_pendingVitals.ContainsKey(key))
                _pendingVitals.Remove(_pendingVitals.MinBy(pair => pair.Value.CapturedAt).Key);
            _pendingVitals[key] = sample;
            if (selected && (_vitalSample is null || capturedAt >= _vitalSample.CapturedAt)) _vitalSample = sample;
        }
    }

    internal string WeightDiagnostic(DateTimeOffset now) =>
        VitalSample(now) is { Vitals: var vitals } ?
            vitals.MaxHealth is not null && vitals.MaxHunger is not null && vitals.MaxStamina is not null
                ? "HP / FOOD / NƯỚC / STAMINA: đọc từ packet Dino hiện tại"
                : "HP / FOOD / NƯỚC / STAMINA: packet game · chờ các giá trị tối đa" :
        _statisticsChannel is null ? "KG: chưa xác định được kênh Dino từ game" :
        WeightSample(now) is { } sample ? now - sample.CapturedAt > NpcapDinosaurWeightDecoder.PendingSampleLifetime
            ? "KG: giữ cân nặng cuối của Dino hiện tại" : "KG: đã nhận cân nặng để tính máu" :
        _weightSample is not null ? "KG: mẫu đã hết hạn, chờ game cập nhật" :
        "KG: đã xác định Dino, chờ khối cân nặng đầy đủ";

    internal bool TryDecodeMovement(
        ParsedBunch bunch,
        DateTimeOffset capturedAt,
        out NpcapPositionSample sample)
    {
        sample = default;
        if (_lockedCandidate is { } locked)
        {
            if (capturedAt - _lastLockedAt > LockedCandidateTimeout)
            {
                // The proven layout went quiet: fall through and scan again so a
                // changed RPC layout can be re-locked instead of staying blind.
                _lockedCandidate = null;
            }
            else if (locked.Channel == bunch.Channel &&
                     TryReadMovementAt(bunch, locked.Offset, out var lockedMove) &&
                     AcceptCandidate(locked, lockedMove, capturedAt, out sample, requireLockEvidence: false))
            {
                _lastLockedAt = capturedAt;
                return true;
            }
            else
            {
                // A larger NetRef handle on respawn changes the movement offset
                // by whole bytes. Drop ownership immediately and prove the new
                // layout again instead of keeping the dead dinosaur for 3 s.
                var changedActor = false;
                if (locked.Channel == bunch.Channel && _movementActorHandle is { } actor)
                    foreach (var shift in new[] { -24, -16, -8, 8, 16, 24 })
                    {
                        var offset = locked.Offset + shift;
                        if (offset >= 0 && TryReadMovementAt(bunch, offset, out _) &&
                            NpcapDinosaurVitalDecoder.TryReadMovementHandle(bunch.Payload, bunch.PayloadBits, offset, out var next) && next != actor)
                        {
                            changedActor = true;
                            break;
                        }
                    }
                if (changedActor)
                {
                    _movementActorHandle = null;
                    _movementActorChannel = null;
                    _candidateActorHandle = null;
                    _actorEvidence = 0;
                    _lockedCandidate = null;
                }
                // Other RPCs share this actor channel. Keep the proven movement
                // layout locked instead of allowing their bytes to create a false
                // candidate while a real movement frame is briefly absent.
                else return false;
            }
        }

        var maximumOffset = Math.Min(384, bunch.PayloadBits - 120);
        for (var offset = 0; offset <= maximumOffset; offset++)
        {
            if (!TryReadMovementAt(bunch, offset, out var move)) continue;
            var key = (bunch.Channel, offset);
            if (AcceptCandidate(key, move, capturedAt, out sample, requireLockEvidence: true))
            {
                _lockedCandidate = key;
                _lastLockedAt = capturedAt;
                return true;
            }
        }

        return false;
    }

    public void SeedLocation(WorldLocation location)
    {
        lock (_candidates)
        {
            _verifiedPlayerAnchor = location;
            _anchorAt = DateTimeOffset.UtcNow;
            (uint Channel, int Offset)? bestKey = null;
            var minDistance = double.MaxValue;

            foreach (var (key, candidate) in _candidates)
            {
                var distance = Math.Sqrt(
                    Math.Pow(candidate.Location.X - location.X, 2) +
                    Math.Pow(candidate.Location.Y - location.Y, 2));
                if (distance < minDistance && distance <= 50_000d)
                {
                    minDistance = distance;
                    bestKey = key;
                }
            }

            if (bestKey is { } best)
            {
                _lockedCandidate = best;
                _lastLockedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                _lockedCandidate = null;
            }
        }
    }

    private bool AcceptCandidate(
        (uint Channel, int Offset) key,
        DecodedMovement move,
        DateTimeOffset capturedAt,
        out NpcapPositionSample sample,
        bool requireLockEvidence)
    {
        sample = default;
        lock (_candidates)
        {
            // The clipboard seed only stays authoritative while frames keep
            // confirming it: a respawn or teleport must be able to move the
            // anchor instead of silencing the decoder for the whole session.
            if (_verifiedPlayerAnchor is { } anchor &&
                capturedAt - _anchorAt <= AnchorHoldDuration &&
                PlanarDistance(move.Location, anchor) > 50_000d)
            {
                return false;
            }

            if (!_candidates.TryGetValue(key, out var previous) ||
                capturedAt - previous.CapturedAt > CandidateExpiry)
            {
                _candidates[key] = MovementCandidate.Start(move, capturedAt);
                return false;
            }

            var wallDelta = (capturedAt - previous.CapturedAt).TotalSeconds;
            var gameDelta = GameTimestampDelta(previous.Timestamp, move.Timestamp);
            var planarDistance = PlanarDistance(move.Location, previous.Location);

            if (gameDelta <= 0f)
            {
                // The same movement frame is sent once per actor and can also be
                // resent, so its timestamp repeats a few hundred milliseconds apart.
                // A repeat with an unchanged location keeps the candidate instead of
                // restarting its streak; anything else with a frozen clock is a wrong
                // bit offset, because the movement RPC always carries a fresh value.
                if (wallDelta is > 0 and <= MaximumFrameSeconds && planarDistance < 1d)
                {
                    return false;
                }

                _candidates[key] = MovementCandidate.Start(move, capturedAt);
                return false;
            }

            var advancing = gameDelta >= MinimumTimestampAdvance &&
                            wallDelta is > 0 &&
                            Math.Abs(gameDelta - wallDelta) <= Math.Max(0.2d, wallDelta * 0.75d);
            var plausible = advancing &&
                            wallDelta <= MaximumFrameSeconds &&
                            planarDistance <= Math.Max(50_000d, wallDelta * 25_000d);

            var advancingStreak = plausible ? previous.AdvancingStreak + 1 : 0;
            _candidates[key] = new MovementCandidate(
                move.Timestamp,
                move.Location,
                capturedAt,
                advancingStreak);

            if (!plausible)
            {
                return false;
            }

            // Locking needs several frames whose game clock tracked the capture
            // clock. A wrong offset can hold a constant or arbitrary float, so it
            // never accumulates that evidence.
            if (requireLockEvidence && advancingStreak < RequiredAdvancingFrames)
            {
                return false;
            }

            _verifiedPlayerAnchor = move.Location;
            _anchorAt = capturedAt;
            sample = new NpcapPositionSample(
                move.Location,
                capturedAt,
                move.Timestamp,
                move.ControlYawDegrees);
            return true;
        }
    }

    // The client movement RPC carries the game clock, which advances in lockstep
    // with real time. Movement timestamps wrap around 240 seconds.
    private static float GameTimestampDelta(float previous, float current)
    {
        var timestampReset = previous > 200f && current is >= 0f and < 5f;
        return timestampReset ? current + 240f - previous : current - previous;
    }

    private static bool IsPlausibleLocation(WorldLocation location) =>
        double.IsFinite(location.X) && double.IsFinite(location.Y) &&
        location.Z is { } z && double.IsFinite(z) &&
        Math.Abs(location.X) <= 700_000d && Math.Abs(location.Y) <= 700_000d &&
        z is >= -80_000d and <= 150_000d;

    private static double PlanarDistance(WorldLocation first, WorldLocation second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));

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

            // The move data serializes the control rotation directly after the
            // location: FRotator::SerializeCompressedShort writes one presence bit
            // per axis followed by that axis' 16-bit value (pitch, yaw, roll).
            double? controlYaw = null;
            double? pitch = null;
            double? roll = null;
            if (reader.Remaining >= 3)
            {
                pitch = reader.ReadCompressedRotationAxisShort();
                controlYaw = reader.ReadCompressedRotationAxisShort();
                roll = reader.ReadCompressedRotationAxisShort();
            }

            if (!float.IsFinite(timestamp) || timestamp is < 1 or > 1_000_000 ||
                !IsPlausibleVector(acceleration, 100_000, 100_000) ||
                !IsPlausibleLocation(location) ||
                (Math.Abs(location.X) < 1_000d && Math.Abs(location.Y) < 1_000d))
            {
                return false;
            }

            // Yaw and pitch stay inside a single revolution and a dinosaur never
            // banks; random bytes at a wrong offset fail these bounds constantly.
            if (controlYaw is { } yaw && (yaw < 0d || yaw >= 360d))
            {
                return false;
            }

            if (pitch is { } pitchDegrees && Math.Abs(SignedAxis(pitchDegrees)) > 90d)
            {
                return false;
            }

            if (roll is { } rollDegrees && Math.Abs(SignedAxis(rollDegrees)) > 60d)
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

    private static double SignedAxis(double degrees) => degrees > 180d ? degrees - 360d : degrees;

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
        var hasExports = reader.ReadBit();
        reader.ReadBit();
        var partial = reader.ReadBit();
        var sequence = reliable ? reader.ReadSerializedInt(1024) : 0;
        var initial = false;
        var final = false;
        var customExports = false;
        if (partial)
        {
            initial = reader.ReadBit();
            customExports = reader.ReadBit();
            final = reader.ReadBit();
        }
        if (open || reliable)
        {
            if (!reader.ReadBit()) return false;
            reader.ReadUInt32Packed();
        }

        var payloadBits = reader.ReadSerializedInt(MaximumBunchBits);
        if (payloadBits > reader.Remaining) return false;
        bunch = new ParsedBunch(channel, payloadBits, reader.ReadBits(payloadBits))
        {
            Reliable = reliable, Sequence = sequence, Partial = partial,
            PartialInitial = initial, PartialFinal = final,
            HasPackageMapExports = hasExports, PartialCustomExportsFinal = customExports,
            Close = close
        };
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

    internal readonly record struct ParsedBunch(uint Channel, int PayloadBits, byte[] Payload)
    {
        public bool Reliable { get; init; }
        public int Sequence { get; init; }
        public bool Partial { get; init; }
        public bool PartialInitial { get; init; }
        public bool PartialFinal { get; init; }
        public bool HasPackageMapExports { get; init; }
        public bool PartialCustomExportsFinal { get; init; }
        public bool Close { get; init; }
    }
    internal readonly record struct DecodedMovement(
        float Timestamp,
        WorldLocation Location,
        double? ControlYawDegrees);
    private readonly record struct MovementCandidate(
        float Timestamp,
        WorldLocation Location,
        DateTimeOffset CapturedAt,
        int AdvancingStreak)
    {
        public static MovementCandidate Start(DecodedMovement move, DateTimeOffset capturedAt) =>
            new(move.Timestamp, move.Location, capturedAt, 0);
    }

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
    double? WorldYawDegrees = null,
    string? ServerEndpoint = null);
