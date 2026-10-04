using System.Net;
using TheIsleOverlay.App;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class NpcapDinosaurWeightDecoderTests
{
    // Sanitized numeric attribute slots from two Cera server updates. Includes
    // presence flags and floats only; no actor identity or session information.
    private const string Cera521 = "1536A37D2A6C46FB8C5812201AB124403462498068C49200D15360F9A1A7C0F2434F81E5879E02CB0F8D5812201AB124403462497068C492E0D08825C1A1114B824323960488462C09108D5812201AB1244004";
    private const string Cera526 = "9F9CA77D3E394FFB24051D204A0A3A40941474802829E800D12FD9F5A15FB2EB4333DFE98766BED30F25051D204A0A3A40941474702829E8E05052D0C1A1A4A083434941078892820E1025051D204A0A3A4004";
    private const string Ptera284 = "F1870D7BE20F1BF604FEAE010AFC5D0314F8BB0628F0770D50C6EA79A18CD5F3420DC310861A86210C05FEAE010AFC5D0314F8BBF627F077ED4FE0EFDA9FC0DFB53F81BF6B80027FD70005FEAE010AFC5D0304";
    private const string Ptera299 = "01351B7B026A36F6646DFC01CADAF80394B5F107286BE30F5033F37CA166E6F942218C178642182F0C656DFC01CADAF80394B5F1F7276BE3EF4FD6C6DF9FAC8DBF3F591B7F80B236FE00656DFC01CADAF80304";
    // Numeric slots from Deino captures: two updates 80.130 seconds apart,
    // and a larger Deino with a different preceding optional-field layout.
    private const string Deino70 = "8FFD0A7B1EFB15F624446E144A88DC289410B951282172A3D08ED7A9A11DAF53433596A8866A2C510D25446E144A88DC289410B941282172835042E406A184C80D4209911B851222370A25446E144A88DC2804";
    private const string Deino73 = "FD0F267BFA1F4CF6A45C9F144AB93E2994727D5228E5FAA4507812ADA1F0245A43E149B486C293680DA55C9F144AB93E2994727D4228E5FA8450CAF509A194EB134229D7278552AE4F0AA55C9F144AB93E2904";
    private const string Deino3275 = "4B15097E962A12FC34B5652A6A6ACB54D4D496A9A8A92D5351C93608A2926D104425DB20884AB6411035B5652A6A6ACB54D4D49699A8A92D3351535B66A2A6B6CC444D6D998A9ADA321535B5652A6A6ACB5404";
    private const int AttributeBits = 660;
    private const int ShortAttributeBits = 528;
    private const string PteraShort96 = "D74A0D7FAE951AFECC8AD0159A15A12BB4523D5868A57AB0D04AF560A195EAC1422BD5838456AA0709AD540F125AA91E24B4523D5868A57AB0D04AF560A195EAC142";
    private const string PteraShort98 = "6B7B0F7FD6F61EFE4C5811169AB0222C741F8A58E83E14B1D07D2862A1FB50C442F7A18884EE431109DD872212BA0F4524741F8A58E83E14B1D07D2862A1FB50C442";
    // Sanitized slots from the live Ptera 83% report: the context is >1.1.
    private const string PteraShort111 = "DF70227FBEE144FE04BBF5160A76EB2D14ECD65B28D8ADB750B05B6FA160B7DE42C16EBD8482DD7A0905BBF5120A76EB2514ECD65B28D8ADB750B05B6FA160B7DE42";
    private const string PteraShort112 = "FB27237FF64F46FECC8702179A0F052E341F0A5C683E14B8D07C2870A1F950E042F3A1C084E6438109CD8702139A0F0526341F0A5C683E14B8D07C2870A1F950E042";
    // These capacity-only tails cannot be attributed to body mass. The old scan
    // accepted them without checking the preceding attribute context.
    private const string DamagedCeraTail = "A721DC874E43B80F2D74B3235AE86647D4782F96A8F15E2C51E3BD18A2C67B31448DF762881AEFC51035DE8B256ABC174BD4782F96A8F15E2C11";
    private const string CapacityOnlyTail = "5B92478AB6248F146D491E29DA923C52B42579A4684BF248D196E451A22DC9A3445B924789B6248F126D491E29DA923C52B42579A4684BF24811";
    private const int CapacityBits = 462;
    private static readonly DateTimeOffset StartedAt = new(2026, 10, 1, 3, 10, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(PteraShort96, 96.95816802978516)]
    [InlineData(PteraShort98, 98.15816497802734)]
    [InlineData(PteraShort111, 111.358154296875)]
    [InlineData(PteraShort112, 112.15814971923828)]
    public void Decode_ReadsTheShortPteraAttributeLayoutCapturedOnOctober2(string hex, double expected)
    {
        var bytes = Convert.FromHexString(hex);
        Assert.True(NpcapDinosaurWeightDecoder.TryDecode(bytes, ShortAttributeBits, out var kg, out var offset));
        Assert.Equal(expected, kg);
        Assert.Equal(133, offset);
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        decoder.TryProcessGamePayload(Packet(8, 2, new NpcapGamePacketDecoder.ParsedBunch(2, ShortAttributeBits, bytes)), StartedAt, out _);
        var readout = new DinosaurVitalReadout();
        readout.UpdateWeight(decoder.WeightSample(StartedAt));
        readout.UpdatePlayer(new() { Class = "Pteranodon", HealthPercent = 80d });
        Assert.Equal(Math.Truncate(expected), readout.Health(StartedAt).Maximum);
        Assert.Equal(Math.Truncate(expected) * .8d, readout.Health(StartedAt).Current);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(99)]
    [InlineData(175)]
    [InlineData(238)]
    [InlineData(280)]
    [InlineData(445)]
    public void Decode_RejectsDamagedShortAttributeBlocks(int damagedBit)
    {
        var bytes = Convert.FromHexString(PteraShort96);
        bytes[damagedBit >> 3] ^= (byte)(1 << (damagedBit & 7));
        Assert.False(NpcapDinosaurWeightDecoder.TryDecode(bytes, ShortAttributeBits, out _, out _));
    }

    [Theory]
    [InlineData(111.358154296875f)]
    [InlineData(55.6790771484375f)]
    public void Decode_RejectsCapacityCopiesUsedAsThePrecedingContext(float capacity)
    {
        var bytes = Convert.FromHexString(PteraShort111);
        SetFloat(bytes, 1, capacity);
        SetFloat(bytes, 34, capacity);
        Assert.False(NpcapDinosaurWeightDecoder.TryDecode(bytes, ShortAttributeBits, out _, out _));
    }

    [Theory]
    [InlineData(Cera521, 521.1729125976562)]
    [InlineData(Cera526, 526.510009765625)]
    [InlineData(Ptera284, 2.8417816162109375)]
    [InlineData(Ptera299, 2.9930219650268555)]
    [InlineData(Deino70, 70.89163208007812)]
    [InlineData(Deino73, 73.96011352539062)]
    [InlineData(Deino3275, 3275.41552734375)]
    public void Decode_UsesTheMassFieldFromCapturedAttributeSlots(string hex, double expected)
    {
        Assert.True(NpcapDinosaurWeightDecoder.TryDecode(Convert.FromHexString(hex), AttributeBits, out var kg, out var offset));
        Assert.Equal(expected, kg);
        Assert.Equal(133, offset);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(884)]
    [InlineData(984)]
    public void Decode_FollowsTheBlockWhenOptionalFieldsMoveItsBitOffset(int prefixBits)
    {
        var payload = JoinBits(prefixBits, Convert.FromHexString(Cera521));
        Assert.True(NpcapDinosaurWeightDecoder.TryDecode(payload, prefixBits + AttributeBits, out var kg, out var offset));
        Assert.Equal(521.1729125976562, kg);
        Assert.Equal(prefixBits + 133, offset);
    }

    [Theory]
    [InlineData(0)] // missing attribute context
    [InlineData(40)] // inconsistent context pair
    [InlineData(99)] // missing current-health scalar
    [InlineData(110)] // inconsistent current-health pair
    [InlineData(198)] // missing first capacity scalar
    [InlineData(238)] // inconsistent capacity pair
    [InlineData(330)] // missing mass capacity
    [InlineData(370)] // inconsistent mass copy
    [InlineData(410)] // incorrect half-mass field
    public void Decode_RejectsAnIncompleteOrInconsistentAttributeBlock(int damagedBit)
    {
        var payload = Convert.FromHexString(Cera521);
        payload[damagedBit >> 3] ^= (byte)(1 << (damagedBit & 7));
        Assert.False(NpcapDinosaurWeightDecoder.TryDecode(payload, AttributeBits, out _, out _));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(659)]
    [InlineData(665)]
    public void Decode_RejectsInvalidOrTruncatedBitLimits(int payloadBits)
    {
        Assert.False(NpcapDinosaurWeightDecoder.TryDecode(Convert.FromHexString(Cera521), payloadBits, out _, out _));
    }

    [Theory]
    [InlineData(DamagedCeraTail)]
    [InlineData(CapacityOnlyTail)]
    public void Decode_RejectsCapacityCopiesWithoutAttributeContext(string hex)
    {
        var payload = new byte[(AttributeBits + 7) / 8];
        // Pad the captured capacity tail to a full bunch, so rejection depends
        // on its absent context rather than the minimum payload size.
        var tail = Convert.FromHexString(hex);
        for (var bit = 0; bit < CapacityBits; bit++)
            if (((tail[bit >> 3] >> (bit & 7)) & 1) != 0)
            {
                var target = 198 + bit;
                payload[target >> 3] |= (byte)(1 << (target & 7));
            }
        Assert.False(NpcapDinosaurWeightDecoder.TryDecode(payload, AttributeBits, out _, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(500)]
    public void Decode_UsesMaximumCapacityWhenCurrentHealthDrops(float currentHealth)
    {
        var payload = Convert.FromHexString(Cera521);
        SetFloat(payload, 67, currentHealth);
        SetFloat(payload, 100, currentHealth);
        Assert.True(NpcapDinosaurWeightDecoder.TryDecode(payload, AttributeBits, out var kg, out _));
        Assert.Equal(521.1729125976562, kg);
    }

    [Fact]
    public void Statistics_DoesNotReplaceThePlayerMassWithAnUnrelatedCapacityTail()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        decoder.ObserveStatistics(Bunch(2, Cera521), StartedAt);
        var updatedAt = StartedAt.AddSeconds(24);
        decoder.ObserveStatistics(new(2, CapacityBits, Convert.FromHexString(DamagedCeraTail)), updatedAt);
        Assert.Equal(521.1729125976562, decoder.WeightSample(updatedAt)!.Value.Kilograms);
        Assert.Equal(StartedAt, decoder.WeightSample(updatedAt)!.Value.CapturedAt);
        Assert.Equal(521.1729125976562, decoder.WeightSample(StartedAt.AddHours(1))!.Value.Kilograms);
    }

    [Fact]
    public void Statistics_DeinoHpStaysAvailableAcrossTheCapturedUpdateInterval()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        var readout = new DinosaurVitalReadout();
        readout.UpdatePlayer(new PlayerTelemetry { Class = "Deinosuchus", HealthPercent = 50d });
        decoder.TryProcessGamePayload(Packet(8, 2, Bunch(2, Deino70)), StartedAt, out _);

        var beforeUpdate = StartedAt.AddSeconds(80);
        readout.UpdateWeight(decoder.WeightSample(beforeUpdate));
        Assert.Equal(35d, readout.Health(beforeUpdate).Current);
        Assert.Equal(70d, readout.Health(beforeUpdate).Maximum);
        Assert.Equal(StartedAt, decoder.WeightSample(beforeUpdate)!.Value.CapturedAt);

        var nextUpdate = StartedAt.AddSeconds(80.130);
        decoder.TryProcessGamePayload(Packet(8, 2, Bunch(2, Deino73)), nextUpdate, out _);
        readout.UpdateWeight(decoder.WeightSample(nextUpdate));
        Assert.Equal(36.5d, readout.Health(nextUpdate).Current);
        Assert.Equal(73d, readout.Health(nextUpdate).Maximum);
        Assert.Equal(nextUpdate, decoder.WeightSample(nextUpdate)!.Value.CapturedAt);
        Assert.Equal(73.96011352539062, decoder.WeightSample(nextUpdate.AddHours(1))!.Value.Kilograms);
    }

    [Fact]
    public void Decode_DoesNotMistakeStableFloatsForMass()
    {
        var writer = new TestBitWriter();
        for (var index = 0; index < 30; index++) writer.WriteSingle(index % 2 == 0 ? 515f : 3336.019f);
        Assert.False(NpcapDinosaurWeightDecoder.TryDecode(writer.ToArray(), writer.Position, out _, out _));
    }

    [Fact]
    public void Decode_RejectsTwoDifferentMassBlocksInOneBunch()
    {
        var payload = JoinBits(0, Convert.FromHexString(Cera521), Convert.FromHexString(Cera526));
        Assert.False(NpcapDinosaurWeightDecoder.TryDecode(payload, 2 * AttributeBits, out var kg, out _));
        Assert.Equal(0, kg);
    }

    [Fact]
    public void Statistics_OnlyReadsTheSelectedActorChannel()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        decoder.ObserveStatistics(Bunch(9, Cera521), StartedAt);
        Assert.Null(decoder.WeightSample(StartedAt));
        decoder.ObserveStatistics(Bunch(2, Cera521), StartedAt);
        Assert.Equal(521.1729125976562, decoder.WeightSample(StartedAt)!.Value.Kilograms);
    }

    [Fact]
    public void Statistics_DoesNotReadAnActorUntilAChannelIsSelected()
    {
        var decoder = new NpcapGamePacketDecoder();
        decoder.ObserveStatistics(Bunch(2, Cera521), StartedAt);
        Assert.Null(decoder.WeightSample(StartedAt));
    }

    [Fact]
    public void Statistics_UsesEarlyReplicationOnlyAfterTheClientIdentifiesTheSameActor()
    {
        var decoder = new NpcapGamePacketDecoder();
        decoder.ObserveStatistics(Bunch(9, Cera526), StartedAt);
        decoder.ObserveStatistics(Bunch(2, Ptera284), StartedAt);
        Assert.Null(decoder.WeightSample(StartedAt));
        decoder.StatisticsChannel = 2;
        Assert.Equal(2.8417816162109375, decoder.WeightSample(StartedAt.AddSeconds(2))!.Value.Kilograms);
        decoder.StatisticsChannel = 9;
        Assert.Null(decoder.WeightSample(StartedAt.AddSeconds(2)));
    }

    [Fact]
    public void Statistics_DoesNotReviveAnExpiredEarlyReplication()
    {
        var decoder = new NpcapGamePacketDecoder();
        decoder.ObserveStatistics(Bunch(2, Ptera284), StartedAt);
        decoder.StatisticsChannel = 2;
        Assert.Null(decoder.WeightSample(StartedAt.AddSeconds(121)));
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(16, 1)]
    [InlineData(16, 2)]
    public void GamePayload_ReadsWeightWithEachSupportedPacketHandlerLayout(int prefixBits, int terminationBits)
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        var packet = Packet(prefixBits, terminationBits, Bunch(2, Ptera284));
        decoder.TryProcessGamePayload(packet, StartedAt, out _);
        Assert.Equal(2.8417816162109375, decoder.WeightSample(StartedAt)!.Value.Kilograms);
    }

    [Theory]
    [InlineData(8, 1)]
    [InlineData(8, 2)]
    [InlineData(16, 1)]
    [InlineData(16, 2)]
    public async Task GamePayload_IdentifiesClientActorAndRecoversItsEarlierWeight(int prefixBits, int terminationBits)
    {
        await using var source = new NpcapGamePositionSource();
        var client = new NpcapGamePacketDecoder();
        var server = new NpcapGamePacketDecoder();
        var clientFlow = new NpcapGamePositionSource.GameFlow(59324, IPAddress.Parse("192.0.2.10"), 7777, false);
        var serverFlow = clientFlow with { Inbound = true };
        source.TrackStatistics(server, serverFlow);
        server.TryProcessGamePayload(Packet(prefixBits, terminationBits, Bunch(9, Cera526), Bunch(2, Ptera284)), StartedAt, out _);
        Assert.Null(server.WeightSample(StartedAt));

        for (var frame = 0; frame < 4; frame++)
        {
            var writer = new TestBitWriter();
            writer.WriteSingle(100f + frame * 0.2f);
            writer.WriteQuantizedVector(0, 0, 0, 10);
            writer.WriteQuantizedVector(-200_000 + frame * 100, 300_000, 10_000, 100);
            writer.WriteCompressedRotator(0, 180, 0);
            source.TrackStatistics(client, clientFlow);
            client.TryProcessGamePayload(Packet(prefixBits, terminationBits,
                new NpcapGamePacketDecoder.ParsedBunch(2, writer.Position, writer.ToArray())), StartedAt.AddMilliseconds(frame * 200), out _);
            source.TrackStatistics(client, clientFlow);
        }
        Assert.Equal(2u, client.LockedChannel);
        source.TrackStatistics(server, serverFlow);
        var sample = server.WeightSample(StartedAt.AddSeconds(1));
        Assert.Equal(2.8417816162109375, sample!.Value.Kilograms);
        Assert.Equal(StartedAt, sample.Value.CapturedAt); // recovering doesn't freshen old data
        source.TrackStatistics(server, serverFlow with { RemotePort = 7778 });
        Assert.Null(server.WeightSample(StartedAt.AddSeconds(1)));
    }

    [Fact]
    public async Task StatisticsSelection_ClearsOwnershipWhenClientFallsBackToChannelLessMovement()
    {
        await using var source = new NpcapGamePositionSource();
        var client = MovementDecoder(2);
        var server = new NpcapGamePacketDecoder();
        var clientFlow = new NpcapGamePositionSource.GameFlow(59324, IPAddress.Parse("192.0.2.10"), 7777, false);
        source.TrackStatistics(client, clientFlow);
        source.TrackStatistics(server, clientFlow with { Inbound = true });
        server.ObserveStatistics(Bunch(2, Ptera284), StartedAt);
        Assert.NotNull(server.WeightSample(StartedAt));
        for (var frame = 0; frame < 4; frame++)
        {
            var writer = new TestBitWriter();
            writer.WriteSingle(104f + frame * 0.2f);
            writer.WriteQuantizedVector(0, 0, 0, 10);
            writer.WriteQuantizedVector(-200_000 + frame * 100, 300_000, 10_000, 100);
            writer.WriteCompressedRotator(0, 180, 0);
            client.TryDecodeMovement(new(uint.MaxValue, writer.Position, writer.ToArray()),
                StartedAt.AddSeconds(4).AddMilliseconds(frame * 200), out _);
        }
        source.TrackStatistics(client, clientFlow);
        source.TrackStatistics(server, clientFlow with { Inbound = true });
        Assert.Null(server.StatisticsChannel);
        Assert.Null(server.WeightSample(StartedAt.AddSeconds(5)));
    }

    [Fact]
    public void GamePayload_ReassemblesWeightAcrossDatagramsBeforePublishingIt()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        var attributes = Convert.FromHexString(Ptera284);
        var first = Fragment(2, attributes, 0, 320, 1023, initial: true);
        var final = Fragment(2, attributes, 320, AttributeBits - 320, 0, final: true);
        decoder.TryProcessGamePayload(Packet(16, 2, first), StartedAt, out _);
        Assert.Null(decoder.WeightSample(StartedAt));
        decoder.TryProcessGamePayload(Packet(16, 2, final), StartedAt.AddMilliseconds(200), out _);
        Assert.Equal(2.8417816162109375, decoder.WeightSample(StartedAt.AddMilliseconds(200))!.Value.Kilograms);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GamePayload_ActorClosureDiscardsItsCachedOrSelectedWeight(bool selected)
    {
        var decoder = new NpcapGamePacketDecoder();
        if (selected) decoder.StatisticsChannel = 2;
        decoder.TryProcessGamePayload(Packet(16, 2, Bunch(2, Ptera284)), StartedAt, out _);
        decoder.TryProcessGamePayload(Packet(16, 2,
            new NpcapGamePacketDecoder.ParsedBunch(2, 0, []) { Close = true }), StartedAt.AddSeconds(1), out _);
        decoder.StatisticsChannel = 2;
        Assert.Null(decoder.WeightSample(StartedAt.AddSeconds(1)));
    }

    [Fact]
    public void GamePayload_DoesNotCommitStatisticsFromAPacketWithABrokenFollowingBunch()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        var packet = Packet(8, 2, Bunch(2, Ptera284),
            new(2, 16, [0xff, 0xff]) { Reliable = true, Sequence = 3 });
        // Keep the outer terminators but truncate the following bunch's payload.
        // Both footer probes must reject the complete packet transaction.
        var writer = new TestBitWriter();
        writer.WriteBits(packet, 0, packet.Length * 8 - 24);
        writer.WriteBit(true);
        writer.WriteBit(true);
        decoder.TryProcessGamePayload(writer.ToArray(), StartedAt, out _);
        Assert.Null(decoder.WeightSample(StartedAt));
    }

    [Fact]
    public void Statistics_ChangingActorClearsThePreviousMass()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        decoder.ObserveStatistics(Bunch(2, Cera521), StartedAt);
        decoder.StatisticsChannel = 9;
        Assert.Null(decoder.WeightSample(StartedAt));
    }

    [Fact]
    public void Statistics_KeepsTheLastValidatedMassBetweenSlowServerUpdates()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        decoder.ObserveStatistics(Bunch(2, Cera521), StartedAt);
        Assert.NotNull(decoder.WeightSample(StartedAt.AddSeconds(24)));
        Assert.Equal(521.1729125976562, decoder.WeightSample(StartedAt.AddHours(1))!.Value.Kilograms);
        Assert.Null(decoder.WeightSample(StartedAt.AddSeconds(-1)));
    }

    [Fact]
    public void Statistics_RecoveredEarlyWeightStaysConfirmedUntilActorReset()
    {
        var decoder = new NpcapGamePacketDecoder();
        decoder.ObserveStatistics(new(2, ShortAttributeBits, Convert.FromHexString(PteraShort112)), StartedAt);
        decoder.StatisticsChannel = 2;
        var recovered = decoder.WeightSample(StartedAt.AddSeconds(2))!.Value;
        Assert.True(recovered.ActorConfirmed);
        Assert.Equal(StartedAt, recovered.CapturedAt);
        Assert.Equal(recovered, decoder.WeightSample(StartedAt.AddHours(3)));
        decoder.StatisticsChannel = null; // flow/ownership lost
        decoder.StatisticsChannel = 2; // same channel number in a new session
        Assert.Null(decoder.WeightSample(StartedAt.AddHours(3)));
    }

    [Fact]
    public void Statistics_PlateauKeepsHpButStillAcceptsTheNextMassChangeAndActorClosure()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        decoder.TryProcessGamePayload(Packet(8, 2,
            new NpcapGamePacketDecoder.ParsedBunch(2, ShortAttributeBits, Convert.FromHexString(PteraShort111))), StartedAt, out _);
        var readout = new DinosaurVitalReadout();
        readout.UpdatePlayer(new() { Class = "Pteranodon", GrowthPercent = 88d, HealthPercent = 100d });
        var later = StartedAt.AddHours(3);
        // Ordinary traffic without a KG block does not refresh the measurement.
        decoder.TryProcessGamePayload(Packet(8, 2, new NpcapGamePacketDecoder.ParsedBunch(2, 16, [0, 0])), later, out _);
        var unchanged = decoder.WeightSample(later)!.Value;
        Assert.Equal(StartedAt, unchanged.CapturedAt);
        Assert.Contains("giữ cân nặng", decoder.WeightDiagnostic(later));
        readout.UpdateWeight(unchanged);
        Assert.Equal(111d, readout.Health(later).Maximum);
        readout.UpdatePlayer(new() { Class = "Pteranodon", GrowthPercent = 100d, HealthPercent = 80d });
        Assert.Equal(88.8d, readout.Health(later).Current!.Value, precision: 8);

        var next = later.AddSeconds(1);
        decoder.TryProcessGamePayload(Packet(8, 2,
            new NpcapGamePacketDecoder.ParsedBunch(2, ShortAttributeBits, Convert.FromHexString(PteraShort112))), next, out _);
        readout.UpdateWeight(decoder.WeightSample(next));
        Assert.Equal(112d, readout.Health(next).Maximum);
        // A newer smaller value is also authoritative; there is no growth-based lock.
        next = next.AddSeconds(1);
        decoder.TryProcessGamePayload(Packet(8, 2,
            new NpcapGamePacketDecoder.ParsedBunch(2, ShortAttributeBits, Convert.FromHexString(PteraShort98))), next, out _);
        readout.UpdateWeight(decoder.WeightSample(next));
        Assert.Equal(98d, readout.Health(next).Maximum);
        next = next.AddSeconds(1);
        decoder.TryProcessGamePayload(Packet(8, 2, new NpcapGamePacketDecoder.ParsedBunch(2, 0, []) { Close = true }), next, out _);
        readout.UpdateWeight(decoder.WeightSample(next));
        Assert.Null(readout.Health(next).Maximum);
        Assert.Equal(80d, readout.Health(next).Percent);
    }

    [Fact]
    public void Statistics_AnOlderPacketCannotRollBackTheMass()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2 };
        decoder.ObserveStatistics(Bunch(2, Cera526), StartedAt.AddSeconds(24));
        decoder.ObserveStatistics(Bunch(2, Cera521), StartedAt);
        Assert.Equal(526.510009765625, decoder.WeightSample(StartedAt.AddSeconds(25))!.Value.Kilograms);
    }

    [Fact]
    public void ConnectionMatch_AllowsTheReplyDirectionButRejectsOtherConnections()
    {
        var outbound = new NpcapGamePositionSource.GameFlow(59324, IPAddress.Parse("192.0.2.10"), 7777, false);
        Assert.True(NpcapGamePositionSource.IsSameConnection(outbound, outbound with { Inbound = true }));
        Assert.True(NpcapGamePositionSource.IsSameConnection(outbound,
            new NpcapGamePositionSource.GameFlow(59324, IPAddress.Parse("192.0.2.10"), 7777, true)));
        Assert.False(NpcapGamePositionSource.IsSameConnection(outbound, outbound with { LocalPort = 59325 }));
        Assert.False(NpcapGamePositionSource.IsSameConnection(outbound, outbound with { RemotePort = 7778 }));
        Assert.False(NpcapGamePositionSource.IsSameConnection(outbound, outbound with { RemoteAddress = IPAddress.Parse("192.0.2.11") }));
    }

    [Fact]
    public async Task StatisticsSelection_DoesNotTrustAServerActorLockOrAnOldClipboardAnchor()
    {
        await using var source = new NpcapGamePositionSource();
        var inbound = MovementDecoder(9);
        source.SeedLocation(new() { X = -200_000, Y = 300_000, Z = 10_000 });
        source.TrackStatistics(inbound,
            new NpcapGamePositionSource.GameFlow(59324, IPAddress.Parse("192.0.2.10"), 7777, true));
        Assert.Null(inbound.StatisticsChannel);
    }

    [Fact]
    public async Task StatisticsSelection_UsesTheClientActorForTheMatchingServerOnly()
    {
        await using var source = new NpcapGamePositionSource();
        var outbound = MovementDecoder(2);
        var clientFlow = new NpcapGamePositionSource.GameFlow(59324, IPAddress.Parse("192.0.2.10"), 7777, false);
        source.TrackStatistics(outbound, clientFlow);
        var inbound = MovementDecoder(9);
        source.TrackStatistics(inbound,
            new NpcapGamePositionSource.GameFlow(59324, IPAddress.Parse("192.0.2.10"), 7777, true));
        Assert.Equal(2u, inbound.StatisticsChannel);
        source.TrackStatistics(inbound, clientFlow with { RemotePort = 7778, Inbound = true });
        Assert.Null(inbound.StatisticsChannel);
    }

    [Fact]
    public void LockedChannel_HidesMovementThatCameWithoutAnActorChannel()
    {
        var decoder = new NpcapGamePacketDecoder();
        for (var frame = 0; frame < 4; frame++)
        {
            var writer = new TestBitWriter();
            writer.WriteSingle(100f + frame * 0.2f);
            writer.WriteQuantizedVector(0, 0, 0, 10);
            writer.WriteQuantizedVector(-200_000 + frame * 100, 300_000, 10_000, 100);
            writer.WriteCompressedRotator(0, 180, 0);
            var bunch = new NpcapGamePacketDecoder.ParsedBunch(0xFFFF_FFFF, writer.Position, writer.ToArray());
            decoder.TryDecodeMovement(bunch, StartedAt.AddMilliseconds(frame * 200), out _);
        }
        Assert.Null(decoder.LockedChannel);
    }

    private static NpcapGamePacketDecoder.ParsedBunch Bunch(uint channel, string hex) =>
        new(channel, AttributeBits, Convert.FromHexString(hex));

    internal static NpcapGamePacketDecoder.ParsedBunch Fragment(uint channel, byte[] bytes,
        int offset, int count, int sequence, bool initial = false, bool final = false)
    {
        var writer = new TestBitWriter();
        writer.WriteBits(bytes, offset, count);
        return new(channel, count, writer.ToArray())
        { Reliable = true, Partial = true, PartialInitial = initial, PartialFinal = final, Sequence = sequence };
    }

    internal static byte[] Packet(int prefixBits, int terminationBits,
        params NpcapGamePacketDecoder.ParsedBunch[] bunches)
    {
        var writer = new TestBitWriter();
        writer.WriteBytes([0x0c]);
        writer.Skip(prefixBits - 8);
        writer.WriteSerializedInt(0, 4);
        writer.WriteSerializedInt(0, 8);
        writer.WriteBit(false);
        writer.WriteBytes(new byte[8]); // notify header and one history word
        writer.WriteBit(false); // packet info
        foreach (var bunch in bunches)
        {
            writer.WriteBit(bunch.Close); // control
            if (bunch.Close)
            {
                writer.WriteBit(false); // open
                writer.WriteBit(true); // close
                writer.WriteSerializedInt(0, 15); // close reason
            }
            writer.WriteBit(false); // paused
            writer.WriteBit(bunch.Reliable);
            writer.WriteUInt32Packed(bunch.Channel);
            writer.WriteBit(bunch.HasPackageMapExports);
            writer.WriteBit(false); // must-be-mapped GUIDs
            writer.WriteBit(bunch.Partial);
            if (bunch.Reliable) writer.WriteSerializedInt(bunch.Sequence, 1024);
            if (bunch.Partial)
            {
                writer.WriteBit(bunch.PartialInitial);
                writer.WriteBit(bunch.PartialCustomExportsFinal);
                writer.WriteBit(bunch.PartialFinal);
            }
            if (bunch.Reliable)
            {
                writer.WriteBit(true); // hardcoded channel name
                writer.WriteUInt32Packed(2);
            }
            writer.WriteSerializedInt(bunch.PayloadBits, 8192);
            writer.WriteBits(bunch.Payload, 0, bunch.PayloadBits);
        }
        for (var bit = 0; bit < terminationBits; bit++) writer.WriteBit(true);
        return writer.ToArray();
    }

    private static NpcapGamePacketDecoder MovementDecoder(uint channel)
    {
        var decoder = new NpcapGamePacketDecoder();
        for (var frame = 0; frame < 4; frame++)
        {
            var writer = new TestBitWriter();
            writer.WriteSingle(100f + frame * 0.2f);
            writer.WriteQuantizedVector(0, 0, 0, 10);
            writer.WriteQuantizedVector(-200_000 + frame * 100, 300_000, 10_000, 100);
            writer.WriteCompressedRotator(0, 180, 0);
            decoder.TryDecodeMovement(new(channel, writer.Position, writer.ToArray()),
                StartedAt.AddMilliseconds(frame * 200), out _);
        }
        Assert.Equal(channel, decoder.LockedChannel);
        return decoder;
    }

    private static byte[] JoinBits(int prefixBits, params byte[][] blocks)
    {
        var payload = new byte[(prefixBits + blocks.Length * AttributeBits + 7) / 8];
        var target = prefixBits;
        foreach (var block in blocks)
        {
            for (var bit = 0; bit < AttributeBits; bit++, target++)
            {
                if (((block[bit >> 3] >> (bit & 7)) & 1) != 0)
                    payload[target >> 3] |= (byte)(1 << (target & 7));
            }
        }
        return payload;
    }

    private static void SetFloat(byte[] payload, int offset, float value)
    {
        var raw = BitConverter.SingleToUInt32Bits(value);
        for (var bit = 0; bit < 32; bit++)
        {
            var target = offset + bit;
            var mask = (byte)(1 << (target & 7));
            if (((raw >> bit) & 1) != 0) payload[target >> 3] |= mask;
            else payload[target >> 3] &= (byte)~mask;
        }
    }
}
