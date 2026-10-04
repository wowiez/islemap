using TheIsleOverlay.App;
using System.Net;

namespace TheIsleOverlay.App.Tests;

public sealed class NpcapDinosaurVitalDecoderTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 4, 5, 40, 0, TimeSpan.Zero);

    [Fact]
    public void SparseMask_PlacesStaminaAtBit23RatherThanBit15()
    {
        var bunch = Attributes(0xd820, (0, 15.5f), (2, 964.7f), (7, 15.5f), (23, 87.8f));
        var update = Assert.Single(NpcapDinosaurVitalDecoder.ReadUpdates(bunch.Payload, bunch.PayloadBits));
        Assert.Equal(0xd820u, update.Handle);
        Assert.Equal(15.5, update.Vitals.Hunger!.Value, 4);
        Assert.Equal(964.7, update.Vitals.Thirst!.Value, 3);
        Assert.Null(update.Vitals.MaxThirst);
        Assert.Equal(87.8, update.Vitals.Stamina!.Value, 4);
        Assert.Null(update.Vitals.Health);
        Assert.Null(update.Vitals.MaxHealth);
    }

    [Fact]
    public void FullMask_ConsumesOtherAttributesAndReadsCurrentAndMaximum()
    {
        var bunch = Attributes(0x12006, (1, 16.6f), (3, 1000f), (5, 2019f), (15, 3.7f),
            (19, .021f), (21, 50.4f), (22, 50.4f), (23, 87.8f), (24, 87.8f), (30, 50.4f));
        var update = Assert.Single(NpcapDinosaurVitalDecoder.ReadUpdates(bunch.Payload, bunch.PayloadBits));
        Assert.Equal(0x12006u, update.Handle);
        Assert.Equal(50.4, update.Vitals.Health!.Value, 4);
        Assert.Equal(50.4, update.Vitals.MaxHealth!.Value, 4);
        Assert.Equal(16.6, update.Vitals.MaxHunger!.Value, 4);
        Assert.Equal(1000, update.Vitals.MaxThirst);
        Assert.Equal(87.8, update.Vitals.MaxStamina!.Value, 4);
    }

    [Fact]
    public void ZeroValue_IsAnUpdateRatherThanMissingOrFull()
    {
        var bunch = Attributes(0x1006, (0, 0), (21, 0), (23, 0));
        var values = Assert.Single(NpcapDinosaurVitalDecoder.ReadUpdates(bunch.Payload, bunch.PayloadBits)).Vitals;
        Assert.Equal(0, values.Health);
        Assert.Equal(0, values.Hunger);
        Assert.Equal(0, values.Stamina);
    }

    [Fact]
    public void InvalidOrTruncatedBlock_DoesNotPublishPartiallyDecodedFields()
    {
        var valid = Attributes(0x1006, (0, 15.5f), (2, 964.7f), (23, 87.8f));
        Assert.Empty(NpcapDinosaurVitalDecoder.ReadUpdates(valid.Payload, valid.PayloadBits - 1));
        var invalid = Attributes(0x1006, (0, 15.5f), (23, float.NaN));
        Assert.Empty(NpcapDinosaurVitalDecoder.ReadUpdates(invalid.Payload, invalid.PayloadBits));
        invalid = Attributes(0x1006, (0, -1f));
        Assert.Empty(NpcapDinosaurVitalDecoder.ReadUpdates(invalid.Payload, invalid.PayloadBits));
    }

    [Fact]
    public void SelectedObject_IgnoresNearbyActorOnTheSameChannelAndRetainsMaxima()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2, StatisticsActorHandle = 0x1000 };
        decoder.ObserveStatistics(Attributes(0x1006, (1, 16.6f), (3, 1000f), (21, 50.4f), (22, 50.4f), (24, 87.8f)), At);
        decoder.ObserveStatistics(Attributes(0x2006, (0, 999), (21, 3000), (22, 3000)), At.AddSeconds(1));
        decoder.ObserveStatistics(Attributes(0x1006, (0, .2f), (2, 964.7f), (21, 30.5f), (23, 50f)), At.AddMinutes(3));
        var sample = decoder.VitalSample(At.AddMinutes(3))!;
        Assert.Equal(0x1000u, sample.ActorHandle);
        Assert.Equal(30.5, sample.Vitals.Health);
        Assert.Equal(50.4, sample.Vitals.MaxHealth!.Value, 4);
        Assert.Equal(16.6, sample.Vitals.MaxHunger!.Value, 4);
        Assert.Equal(1000, sample.Vitals.MaxThirst);
        Assert.Equal(.2, sample.Vitals.Hunger!.Value, 4);
    }

    [Fact]
    public void Respawn_DropsPreviousObjectEvenWhenChannelStaysTheSame()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2, StatisticsActorHandle = 0x1000 };
        decoder.ObserveStatistics(Attributes(0x1006, (21, 50.4f), (22, 50.4f)), At);
        decoder.StatisticsActorHandle = 0x2000;
        Assert.Null(decoder.VitalSample(At));
        decoder.ObserveStatistics(Attributes(0x1006, (21, 0)), At.AddSeconds(1));
        Assert.Null(decoder.VitalSample(At.AddSeconds(1)));
        decoder.ObserveStatistics(Attributes(0x2006, (21, 6.3f)), At.AddSeconds(2));
        var sample = decoder.VitalSample(At.AddSeconds(2))!;
        Assert.Equal(6.3, sample.Vitals.Health!.Value, 4);
        Assert.Null(sample.Vitals.MaxHealth);
        decoder.StatisticsChannel = null;
        Assert.Null(decoder.VitalSample(At.AddSeconds(3)));
    }

    [Fact]
    public void InitialReplication_IsRecoveredOnlyForConfirmedObjectBeforeExpiry()
    {
        var decoder = new NpcapGamePacketDecoder();
        decoder.ObserveStatistics(Attributes(0x1006, (21, 50.4f), (22, 50.4f)), At);
        Assert.Null(decoder.VitalSample(At));
        decoder.StatisticsChannel = 2;
        decoder.StatisticsActorHandle = 0x1000;
        Assert.NotNull(decoder.VitalSample(At.AddSeconds(1)));
        var late = new NpcapGamePacketDecoder();
        late.ObserveStatistics(Attributes(0x1006, (21, 50.4f)), At);
        late.StatisticsChannel = 2;
        late.StatisticsActorHandle = 0x1000;
        Assert.Null(late.VitalSample(At.AddMinutes(3)));
    }

    [Fact]
    public void OlderCapture_DoesNotRollBackSelectedValues()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2, StatisticsActorHandle = 0x1000 };
        decoder.ObserveStatistics(Attributes(0x1006, (21, 30f)), At.AddSeconds(2));
        decoder.ObserveStatistics(Attributes(0x1006, (21, 50f)), At.AddSeconds(1));
        Assert.Equal(30, decoder.VitalSample(At.AddSeconds(2))!.Vitals.Health);
    }

    [Fact]
    public void ThirstCapacity_DefaultsOnlyUntilAnExplicitCapacityIsReplicated()
    {
        var decoder = new NpcapGamePacketDecoder { StatisticsChannel = 2, StatisticsActorHandle = 0x1000 };
        decoder.ObserveStatistics(Attributes(0x1006, (2, 964.7f)), At);
        Assert.Equal(1000, decoder.VitalSample(At)!.Vitals.MaxThirst);
        decoder.ObserveStatistics(Attributes(0x1006, (3, 1200)), At.AddSeconds(1));
        decoder.ObserveStatistics(Attributes(0x1006, (2, 950)), At.AddSeconds(2));
        Assert.Equal(1200, decoder.VitalSample(At.AddSeconds(2))!.Vitals.MaxThirst);
    }

    [Theory]
    [InlineData(0xd81a, 34)]
    [InlineData(0xd5d2, 42)]
    [InlineData(0x12000, 34)]
    public void MovementOwnership_RequiresRpcFramingAndSupportsHandleWidth(int handle, int suffixBits)
    {
        var writer = new TestBitWriter();
        writer.Skip(101);
        Bits(writer, 0x10000, 17);
        var bytes = handle > 0xffff ? 3 : 2;
        Bits(writer, (uint)(bytes - 1), 3);
        Bits(writer, (uint)handle, bytes * 8);
        Bits(writer, 0x20008, 22);
        writer.Skip(suffixBits - 22);
        var offset = writer.Position;
        writer.WriteSingle(100);
        Assert.True(NpcapDinosaurVitalDecoder.TryReadMovementHandle(writer.ToArray(), writer.Position, offset, out var actual));
        Assert.Equal((uint)handle, actual);
        var damaged = writer.ToArray();
        damaged[117 >> 3] &= unchecked((byte)~(1 << (117 & 7)));
        Assert.False(NpcapDinosaurVitalDecoder.TryReadMovementHandle(damaged, writer.Position, offset, out _));
    }

    internal static NpcapGamePacketDecoder.ParsedBunch Attributes(uint handle, params (int Field, float Value)[] fields)
    {
        var writer = new TestBitWriter();
        writer.Skip(33);
        var bytes = handle > 0xffff ? 3 : 2;
        Bits(writer, (uint)(bytes - 1), 3);
        Bits(writer, handle, bytes * 8);
        Bits(writer, 0x28, 10);
        uint mask = 0, selector = 0;
        foreach (var field in fields) mask |= 1u << field.Field;
        for (var index = 0; index < 4; index++) if (((mask >> (index * 8)) & 255) != 0) selector |= 1u << index;
        Bits(writer, selector, 4);
        for (var index = 0; index < 4; index++) if ((selector & (1u << index)) != 0) Bits(writer, (mask >> (index * 8)) & 255, 8);
        foreach (var field in fields.OrderBy(field => field.Field))
            for (var component = 0; component < 2; component++)
            {
                writer.WriteBit(field.Value != 0);
                if (field.Value != 0) writer.WriteSingle(field.Value);
            }
        return new(2, writer.Position, writer.ToArray());
    }

    private static void Bits(TestBitWriter writer, uint value, int count) => writer.WriteBits(BitConverter.GetBytes(value), 0, count);

    [Fact]
    public async Task GamePackets_ConfirmOwnObjectAcrossRespawnAndRejectAnotherConnection()
    {
        await using var source = new NpcapGamePositionSource();
        var outbound = new NpcapGamePacketDecoder();
        var inbound = new NpcapGamePacketDecoder();
        var flow = new NpcapGamePositionSource.GameFlow(55000, IPAddress.Parse("192.0.2.10"), 7777, false);
        inbound.TryProcessGamePayload(NpcapDinosaurWeightDecoderTests.Packet(16, 2,
            Attributes(0x1006, (21, 50.4f), (22, 50.4f))), At, out _);
        for (var frame = 0; frame < 6; frame++)
        {
            var at = At.AddMilliseconds(frame * 200);
            outbound.TryProcessGamePayload(NpcapDinosaurWeightDecoderTests.Packet(16, 2, Move(0x1000, frame)), at, out _);
            source.TrackStatistics(outbound, flow);
        }
        Assert.Equal(0x1000u, outbound.MovementActorHandle);
        source.TrackStatistics(inbound, flow with { Inbound = true });
        Assert.Equal(0x1000u, inbound.StatisticsActorHandle);
        Assert.Equal(50.4, inbound.VitalSample(At.AddSeconds(2))!.Vitals.Health!.Value, 4);
        source.TrackStatistics(inbound, flow with { Inbound = true, RemotePort = 7778 });
        Assert.Null(inbound.VitalSample(At.AddSeconds(2)));
        source.TrackStatistics(inbound, flow with { Inbound = true });
        for (var frame = 6; frame < 10; frame++)
        {
            outbound.TryProcessGamePayload(NpcapDinosaurWeightDecoderTests.Packet(16, 2, Move(0x12000, frame)), At.AddMilliseconds(frame * 200), out _);
            source.TrackStatistics(outbound, flow);
        }
        source.TrackStatistics(inbound, flow with { Inbound = true });
        Assert.Equal(0x12000u, inbound.StatisticsActorHandle);
        Assert.Null(inbound.VitalSample(At.AddSeconds(2)));
        inbound.TryProcessGamePayload(NpcapDinosaurWeightDecoderTests.Packet(16, 2,
            Attributes(0x12006, (21, 6.3f), (22, 6.3f), (0, .2f), (1, 2.1f))), At.AddSeconds(3), out _);
        Assert.Equal(6.3, inbound.VitalSample(At.AddSeconds(3))!.Vitals.Health!.Value, 4);
        inbound.TryProcessGamePayload(NpcapDinosaurWeightDecoderTests.Packet(16, 2,
            new NpcapGamePacketDecoder.ParsedBunch(2, 0, []) { Close = true }), At.AddSeconds(4), out _);
        Assert.Null(inbound.VitalSample(At.AddSeconds(4)));
    }

    private static NpcapGamePacketDecoder.ParsedBunch Move(uint handle, int frame)
    {
        var writer = new TestBitWriter();
        writer.Skip(101);
        Bits(writer, 0x10000, 17);
        var bytes = handle > 0xffff ? 3 : 2;
        Bits(writer, (uint)(bytes - 1), 3);
        Bits(writer, handle, bytes * 8);
        Bits(writer, 0x20008, 22);
        writer.Skip(12);
        writer.WriteSingle(100 + frame * .2f);
        writer.WriteQuantizedVector(0, 0, 0, 10);
        writer.WriteQuantizedVector(-200_000, 300_000, 10_000, 100);
        writer.WriteCompressedRotator(0, 180, 0);
        return new(2, writer.Position, writer.ToArray());
    }
}
