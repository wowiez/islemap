using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public sealed class SingleInstanceLeaseTests
{
    [Fact]
    public void TryAcquire_BlocksASecondInstanceUntilTheOwnerExits()
    {
        var name = $@"Local\IsleLiveMap.Tests.{Guid.NewGuid():N}";

        Assert.True(SingleInstanceLease.TryAcquire(name, out var first));
        Assert.NotNull(first);

        Assert.False(SingleInstanceLease.TryAcquire(name, out var competing));
        Assert.Null(competing);

        first.Dispose();

        Assert.True(SingleInstanceLease.TryAcquire(name, out var replacement));
        replacement?.Dispose();
    }

    [Fact]
    public void TryAcquire_RejectsAnEmptyMutexName()
    {
        Assert.Throws<ArgumentException>(() =>
            SingleInstanceLease.TryAcquire(" ", out _));
    }
}
