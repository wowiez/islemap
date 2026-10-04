using TheIsleOverlay.App;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App.Tests;

public sealed class DinosaurVitalReadoutTests
{
    private static readonly DateTimeOffset CapturedAt = new(2026, 10, 1, 3, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Health_CombinesWebPercentAndPacketWeightInEitherArrivalOrder(bool weightFirst)
    {
        var readout = new DinosaurVitalReadout();
        var player = new PlayerTelemetry { HealthPercent = 80d, BloodPercent = 25d };
        var weight = Weight(660.5d);
        if (weightFirst)
        {
            readout.UpdateWeight(weight);
            Assert.Null(readout.Health(CapturedAt).Current);
            readout.UpdatePlayer(player);
        }
        else
        {
            readout.UpdatePlayer(player);
            Assert.Null(readout.Health(CapturedAt).Current);
            readout.UpdateWeight(weight);
        }

        var health = readout.Health(CapturedAt);
        Assert.Equal(528d, health.Current!.Value, precision: 8);
        Assert.Equal(660d, health.Maximum);
        Assert.Equal(80d, health.Percent);
        Assert.True(health.FromWeight);
    }

    [Fact]
    public void Health_TruncatesKilogramsBeforeTheCalculation()
    {
        var readout = Readout(665.1263427734375d, 67.75d);
        Assert.Equal(450.5375d, readout.Health(CapturedAt).Current!.Value, precision: 8);
        Assert.Equal(665d, readout.Health(CapturedAt).Maximum);
    }

    [Fact]
    public void Health_UpdatesWhenEitherInputChanges()
    {
        var readout = Readout(660.5d, 80d);
        readout.UpdatePlayer(new() { HealthPercent = 50d });
        Assert.Equal(330d, readout.Health(CapturedAt).Current);
        readout.UpdateWeight(Weight(700d) with { CapturedAt = CapturedAt.AddSeconds(24) });
        Assert.Equal(350d, readout.Health(CapturedAt.AddSeconds(24)).Current);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(0.5d, 3.3d)]
    [InlineData(1d, 6.6d)]
    [InlineData(100d, 660d)]
    public void Health_TreatsZeroAndLowWebPercentagesAsPercentages(double percent, double expected)
    {
        var health = Readout(660.5d, percent).Health(CapturedAt);
        Assert.Equal(expected, health.Current!.Value, precision: 8);
        Assert.Equal(percent, health.Percent);
    }

    [Fact]
    public void Health_KeepsNativeCurrentAndMaximumWhenTheProviderHasThem()
    {
        var readout = Readout(660.5d, 80d);
        readout.UpdatePlayer(new()
        {
            HealthPercent = 80d,
            ExactVitals = new() { Health = 900d, MaxHealth = 1_000d }
        });
        var health = readout.Health(CapturedAt);
        Assert.Equal(900d, health.Current);
        Assert.Equal(1_000d, health.Maximum);
        Assert.Equal(90d, health.Percent);
        Assert.False(health.FromWeight);
    }

    [Fact]
    public void Health_AnExpiredUnconfirmedWeightLeavesOnlyTheWebPercentage()
    {
        var readout = Readout(660.5d, 80d);
        readout.UpdateWeight(Weight(660.5d) with { ActorConfirmed = false });
        Assert.Equal(528d, readout.Health(CapturedAt.AddSeconds(24)).Current!.Value, precision: 8);
        Assert.Equal(528d, readout.Health(CapturedAt.AddSeconds(80)).Current!.Value, precision: 8);
        var health = readout.Health(CapturedAt.AddSeconds(121));
        Assert.Null(readout.Kilograms(CapturedAt.AddSeconds(121)));
        Assert.Null(health.Current);
        Assert.Null(health.Maximum);
        Assert.Equal(80d, health.Percent);
    }

    [Theory]
    [InlineData(75d)]
    [InlineData(88d)]
    public void Health_StableActorWeightKeepsWorkingAsWebHealthAndGrowthChange(double plateauGrowth)
    {
        var readout = new DinosaurVitalReadout();
        var player = new PlayerTelemetry { Class = "Pteranodon", GrowthPercent = plateauGrowth, HealthPercent = 100d };
        readout.UpdatePlayer(player);
        readout.UpdateWeight(Weight(113.758148d));
        Assert.Equal(113d, readout.Health(CapturedAt).Maximum);
        var later = CapturedAt.AddHours(3);
        readout.UpdatePlayer(player with { GrowthPercent = 100d, HealthPercent = 50d });
        Assert.Equal(113.758148d, readout.Kilograms(later));
        Assert.Equal(113d, readout.Health(later).Maximum);
        Assert.Equal(56.5d, readout.Health(later).Current);
        readout.UpdateWeight(null); // stream, actor or source no longer available
        Assert.Null(readout.Health(later).Current);
        Assert.Equal(50d, readout.Health(later).Percent);
    }

    [Fact]
    public void Health_DisablingNpcapRestoresTheWebPercentage()
    {
        var readout = Readout(660.5d, 80d);
        readout.UpdateWeight(null);
        Assert.Null(readout.Kilograms(CapturedAt));
        var health = readout.Health(CapturedAt);
        Assert.Null(health.Current);
        Assert.Equal(80d, health.Percent);
    }

    [Fact]
    public void Health_WebDisconnectDoesNotShowHpFromAnOldPlayer()
    {
        var readout = Readout(660.5d, 80d);
        readout.UpdatePlayer(null);
        readout.UpdateWeight(Weight(700d));
        Assert.Equal(700d, readout.Kilograms(CapturedAt));
        Assert.Equal(default, readout.Health(CapturedAt));
    }

    [Fact]
    public void Health_DoesNotUseBloodPercentageAsHealthPercentage()
    {
        var readout = Readout(660.5d, 80d);
        readout.UpdatePlayer(new() { BloodPercent = 80d });
        Assert.Equal(default, readout.Health(CapturedAt));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Health_NonFiniteWebValuesDoNotProduceHp(double percent)
    {
        var health = Readout(660.5d, percent).Health(CapturedAt);
        Assert.Null(health.Current);
        Assert.Null(health.Percent);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Health_InvalidWeightsLeaveOnlyTheWebPercentage(double kilograms)
    {
        var readout = Readout(kilograms, 80d);
        Assert.Null(readout.Kilograms(CapturedAt));
        Assert.Null(readout.Health(CapturedAt).Current);
        Assert.Equal(80d, readout.Health(CapturedAt).Percent);
    }

    [Fact]
    public void Health_FutureSamplesAreUnavailable()
    {
        var readout = Readout(660.5d, 80d);
        Assert.Null(readout.Kilograms(CapturedAt.AddSeconds(-1)));
        Assert.Null(readout.Health(CapturedAt.AddSeconds(-1)).Current);
    }

    [Theory]
    [InlineData(1.903d, 1d)]
    [InlineData(2.7d, 2d)]
    [InlineData(2.993d, 2d)]
    [InlineData(3d, 3d)]
    [InlineData(0.9d, 0d)]
    public void Health_SmallPteraUsesWholeKilogramsWithoutRoundingUp(double kilograms, double expected)
    {
        var health = Readout(kilograms, 100d).Health(CapturedAt);
        Assert.True(health.FromWeight);
        Assert.Equal(expected, health.Current);
        Assert.Equal(expected, health.Maximum);
        Assert.Equal(100d, health.Percent);
    }

    [Fact]
    public void NativeVitals_UpdateEveryBarEvenWithoutWebsitePlayerOrKilograms()
    {
        var readout = new DinosaurVitalReadout();
        readout.UpdateVitals(new(0x1000, CapturedAt, new()
        {
            Health = 50.4, MaxHealth = 50.4, Hunger = 15.5, MaxHunger = 16.6,
            Thirst = 964.7, MaxThirst = 1000, Stamina = 87.8, MaxStamina = 87.8
        }));
        Assert.Equal(50.4, readout.Health(CapturedAt).Current);
        Assert.Equal(100, readout.Health(CapturedAt).Percent);
        Assert.False(readout.Health(CapturedAt).FromWeight);
        Assert.Equal(15.5, readout.Hunger(CapturedAt).Current);
        Assert.Equal(15.5 / 16.6 * 100, readout.Hunger(CapturedAt).Percent!.Value, 6);
        Assert.Equal(96.47, readout.Thirst(CapturedAt).Percent!.Value, 6);
        Assert.Equal(100, readout.Stamina(CapturedAt).Percent);
        readout.UpdateVitals(null);
        Assert.Null(readout.Health(CapturedAt).Current);
        Assert.Null(readout.Hunger(CapturedAt).Current);
        Assert.Null(readout.Thirst(CapturedAt).Current);
        Assert.Null(readout.Stamina(CapturedAt).Current);
    }

    [Fact]
    public void NativeVitals_OverrideDelayedWebSnapshotsIncludingEmptyStomach()
    {
        var readout = Readout(300, 100);
        readout.UpdateVitals(new(0x1000, CapturedAt, new() { Health = 6.3, MaxHealth = 6.3, Hunger = 0, MaxHunger = 2.1 }));
        readout.UpdatePlayer(new()
        {
            HealthPercent = 100, HungerPercent = 100,
            ExactVitals = new() { Health = 300, MaxHealth = 300, Hunger = 2.1, MaxHunger = 2.1 }
        });
        Assert.Equal(6.3, readout.Health(CapturedAt).Current);
        Assert.Equal(0, readout.Hunger(CapturedAt).Current);
        Assert.Equal(0, readout.Hunger(CapturedAt).Percent);
    }

    [Fact]
    public void NativeVitals_WithoutMaximumRetainCurrentWithoutEstimatingCapacityFromKg()
    {
        var readout = Readout(300, 100);
        readout.UpdateVitals(new(0x1000, CapturedAt, new() { Health = 6.3, Hunger = .2 }));
        Assert.Equal(6.3, readout.Health(CapturedAt).Current);
        Assert.Null(readout.Health(CapturedAt).Maximum);
        Assert.False(readout.Health(CapturedAt).FromWeight);
        Assert.Equal(.2, readout.Hunger(CapturedAt).Current);
        Assert.Null(readout.Hunger(CapturedAt).Maximum);
        Assert.Null(readout.Hunger(CapturedAt.AddSeconds(-1)).Current);
    }

    [Fact]
    public void NativePartialRow_DoesNotBorrowCapacityFromAnOldWebsiteDinosaur()
    {
        var readout = new DinosaurVitalReadout();
        readout.UpdatePlayer(new() { HealthPercent = 100, HungerPercent = 100,
            ExactVitals = new() { Health = 300, MaxHealth = 300, Hunger = 10, MaxHunger = 20 } });
        readout.UpdateVitals(new(0x2000, CapturedAt, new() { Health = 1.9, Hunger = .2 }));
        Assert.Equal(1.9, readout.Health(CapturedAt).Current);
        Assert.Null(readout.Health(CapturedAt).Maximum);
        Assert.Equal(100, readout.Health(CapturedAt).Percent);
        Assert.Equal(.2, readout.Hunger(CapturedAt).Current);
        Assert.Null(readout.Hunger(CapturedAt).Maximum);
        Assert.Equal(100, readout.Hunger(CapturedAt).Percent);
    }

    [Fact]
    public void PartialPackets_UseWebPercentUntilEachRowHasCapacity()
    {
        var readout = new DinosaurVitalReadout();
        readout.UpdateVitals(new(0x2000, CapturedAt, new() { Health = 5, Hunger = .2, Thirst = 964.7, Stamina = 87.8 }));
        Assert.Null(readout.Health(CapturedAt).Percent);
        Assert.Null(readout.Hunger(CapturedAt).Percent);
        readout.UpdatePlayer(new() { HealthPercent = 80, HungerPercent = .5, ThirstPercent = 96, StaminaPercent = 87 });
        Assert.Equal(80, readout.Health(CapturedAt).Percent);
        Assert.Equal(.5, readout.Hunger(CapturedAt).Percent);
        Assert.Equal(96, readout.Thirst(CapturedAt).Percent);
        Assert.Equal(87, readout.Stamina(CapturedAt).Percent);
        Assert.Null(readout.Health(CapturedAt).Maximum);
        readout.UpdateVitals(new(0x2000, CapturedAt, new()
        {
            Health = 5, MaxHealth = 10, Hunger = .2, MaxHunger = 2,
            Thirst = 964.7, MaxThirst = 1000, Stamina = 87.8, MaxStamina = 100
        }));
        Assert.Equal(50, readout.Health(CapturedAt).Percent);
        Assert.Equal(10, readout.Hunger(CapturedAt).Percent);
        Assert.Equal(96.47, readout.Thirst(CapturedAt).Percent!.Value, 6);
        Assert.Equal(87.8, readout.Stamina(CapturedAt).Percent!.Value, 6);
        readout.UpdateVitals(null);
        Assert.Equal(80, readout.Health(CapturedAt).Percent);
        readout.UpdatePlayer(null);
        Assert.Null(readout.Health(CapturedAt).Percent);
        Assert.Null(readout.Hunger(CapturedAt).Percent);
    }

    private static DinosaurVitalReadout Readout(double kilograms, double percent)
    {
        var readout = new DinosaurVitalReadout();
        readout.UpdateWeight(Weight(kilograms));
        readout.UpdatePlayer(new() { HealthPercent = percent });
        return readout;
    }

    private static NpcapWeightSample Weight(double kilograms) => new(kilograms, CapturedAt, 2, 885) { ActorConfirmed = true };
}
