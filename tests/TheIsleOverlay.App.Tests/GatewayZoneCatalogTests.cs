using System.IO;

namespace TheIsleOverlay.App.Tests;

public sealed class GatewayZoneCatalogTests
{
    [Fact]
    public void BundledCatalog_ContainsCurrentGatewayPrimeZones()
    {
        using var stream = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "GatewayZones.json"));

        var zones = GatewayZoneCatalog.Load(stream);

        Assert.Equal(80, zones.Count);
        Assert.Contains(zones, zone => zone.Name == "Delta (MMZ)" && zone.Kind == SbtcZoneKind.Migration);
        Assert.Contains(zones, zone => zone.Name == "East Jungle" && zone.Kind == SbtcZoneKind.Migration);
        Assert.Contains(zones, zone => zone.Name == "Delta" && zone.Kind == SbtcZoneKind.Patrol);
        Assert.All(zones, zone => Assert.True(zone.Points.Count >= 3));
    }
}
