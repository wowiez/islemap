using System.IO;
using System.Resources;

namespace TheIsleOverlay.App.Tests;

public sealed class GatewayZoneCatalogTests
{
    [Theory]
    [InlineData("assets/islepilotzones.json")]
    [InlineData("assets/gatewayzones.json")]
    public void PackagedCatalog_NewInstallHasRegionsWithoutLocalFilesOrApi(string resourceName)
    {
        var assembly = typeof(MainWindow).Assembly;
        using var resources = assembly.GetManifestResourceStream("IsleLiveMap.g.resources");
        Assert.NotNull(resources);
        using var reader = new ResourceReader(resources);
        var entry = reader.GetEnumerator();
        Stream? catalog = null;
        while (entry.MoveNext())
        {
            if (string.Equals(entry.Key as string, resourceName, StringComparison.Ordinal))
            {
                catalog = Assert.IsAssignableFrom<Stream>(entry.Value);
                break;
            }
        }

        Assert.NotNull(catalog);
        var zones = GatewayZoneCatalog.Load(catalog);
        Assert.True(new OverlayLayoutSettings().ShowMapZones);
        var visible = SbtcZoneOverlay.Create(null, [], zones);
        Assert.Contains(visible, zone => zone.Kind == SbtcZoneKind.Patrol);
        Assert.Contains(visible, zone => zone.Kind == SbtcZoneKind.Migration);
        Assert.All(visible, zone => Assert.All(zone.Points, point =>
        {
            Assert.InRange(point.Left, 0d, 1d);
            Assert.InRange(point.Top, 0d, 1d);
        }));
    }

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
