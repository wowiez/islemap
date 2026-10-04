using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class NpcapBunchAssemblerTests
{
    private static readonly DateTimeOffset StartedAt = DateTimeOffset.UtcNow;
    private static readonly byte[] Payload = Enumerable.Range(0, 100).Select(index => (byte)index).ToArray();

    [Fact]
    public void ReassemblesByteAlignedFragmentsAndTheFinalPartialByte()
    {
        var assembler = new NpcapBunchAssembler();
        Assert.False(assembler.TryAssemble(Fragment(2, 0, 320, 10, initial: true), StartedAt, out _));
        Assert.False(assembler.TryAssemble(Fragment(2, 320, 320, 11), StartedAt.AddMilliseconds(20), out _));
        Assert.True(assembler.TryAssemble(Fragment(2, 640, 157, 12, final: true), StartedAt.AddMilliseconds(40), out var bunch));
        Assert.Equal(797, bunch.PayloadBits);
        Assert.Equal(2u, bunch.Channel);
        Assert.Equal(Payload.AsSpan(0, 99).ToArray(), bunch.Payload.AsSpan(0, 99).ToArray());
        Assert.Equal(Payload[99] & 31, bunch.Payload[99]);
    }

    [Theory]
    [InlineData(12, 0)] // missing fragment
    [InlineData(9, 0)] // reordered fragment
    [InlineData(11, 6)] // expired assembly
    public void DiscardsBrokenFragmentChains(int finalSequence, int seconds)
    {
        var assembler = new NpcapBunchAssembler();
        assembler.TryAssemble(Fragment(2, 0, 320, 10, initial: true), StartedAt, out _);
        Assert.False(assembler.TryAssemble(Fragment(2, 320, 320, finalSequence, final: true),
            StartedAt.AddSeconds(seconds), out _));
    }

    [Fact]
    public void DoesNotCombineDifferentActorChannels()
    {
        var assembler = new NpcapBunchAssembler();
        assembler.TryAssemble(Fragment(2, 0, 320, 10, initial: true), StartedAt, out _);
        Assert.False(assembler.TryAssemble(Fragment(9, 320, 320, 11, final: true), StartedAt, out _));
        Assert.True(assembler.TryAssemble(Fragment(2, 320, 320, 11, final: true), StartedAt, out _));
    }

    [Fact]
    public void IgnoresExactRetransmissionsWithoutAppendingThemTwice()
    {
        var assembler = new NpcapBunchAssembler();
        var initial = Fragment(2, 0, 320, 10, initial: true);
        assembler.TryAssemble(initial, StartedAt, out _);
        Assert.False(assembler.TryAssemble(initial, StartedAt.AddMilliseconds(10), out _));
        Assert.True(assembler.TryAssemble(Fragment(2, 320, 320, 11, final: true), StartedAt.AddMilliseconds(20), out var bunch));
        Assert.Equal(640, bunch.PayloadBits);
    }

    [Fact]
    public void DropsConflictingRetransmissions()
    {
        var assembler = new NpcapBunchAssembler();
        var initial = Fragment(2, 0, 320, 10, initial: true);
        assembler.TryAssemble(initial, StartedAt, out _);
        assembler.TryAssemble(initial with { Payload = new byte[40] }, StartedAt.AddMilliseconds(10), out _);
        Assert.False(assembler.TryAssemble(Fragment(2, 320, 320, 11, final: true), StartedAt.AddMilliseconds(20), out _));
    }

    [Theory]
    [InlineData(false, false, false)] // unreliable sequence not supported
    [InlineData(true, true, false)] // export serialization isn't raw actor data
    [InlineData(true, false, true)] // custom exports aren't raw actor data
    public void DoesNotGuessUnsupportedFragmentFormats(bool reliable, bool exports, bool customExports)
    {
        var assembler = new NpcapBunchAssembler();
        var fragment = Fragment(2, 0, 320, 10, initial: true, final: true) with
        { Reliable = reliable, HasPackageMapExports = exports, PartialCustomExportsFinal = customExports };
        Assert.False(assembler.TryAssemble(fragment, StartedAt, out _));
    }

    private static NpcapGamePacketDecoder.ParsedBunch Fragment(uint channel, int offset,
        int count, int sequence, bool initial = false, bool final = false) =>
        NpcapDinosaurWeightDecoderTests.Fragment(channel, Payload, offset, count, sequence, initial, final);
}
