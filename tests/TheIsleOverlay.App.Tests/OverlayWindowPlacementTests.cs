using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayWindowPlacementTests
{
    private static readonly OverlayDisplayArea[] ThreeMonitors =
    [
        new(new Rect(0d, 0d, 1920d, 1032d), IsPrimary: true),
        new(new Rect(1920d, -836d, 1080d, 1872d)),
        new(new Rect(-1920d, 0d, 1920d, 1032d))
    ];
    private static readonly Size HudSize = new(318d, 480d);

    [Fact]
    public void VirtualDesktopGap_ReturnsToThePrimaryMonitor()
    {
        var restored = OverlayWindowPlacement.KeepVisible(new Point(-1920d, -836d), HudSize, ThreeMonitors);
        Assert.Equal(new Point(1578d, 70d), restored);
        Assert.True(ThreeMonitors[0].WorkArea.Contains(new Rect(restored, HudSize)));
    }

    [Theory]
    [InlineData(-1800d, 70d)]
    [InlineData(2000d, -700d)]
    [InlineData(1578d, 70d)]
    public void ValidPlacementOnEachMonitor_IsPreserved(double left, double top)
    {
        var position = new Point(left, top);
        Assert.Equal(position, OverlayWindowPlacement.KeepVisible(position, HudSize, ThreeMonitors));
    }

    [Fact]
    public void DisconnectedMonitor_ReturnsToTheRemainingMonitor()
    {
        Assert.Equal(new Point(1578d, 70d), OverlayWindowPlacement.KeepVisible(
            new Point(-1800d, 70d), HudSize, [ThreeMonitors[0]]));
    }

    [Fact]
    public void PartiallyVisibleHud_IsFittedToItsMonitor()
    {
        Assert.Equal(new Point(1602d, 552d), OverlayWindowPlacement.KeepVisible(
            new Point(1800d, 900d), HudSize, [ThreeMonitors[0]]));
    }

    [Fact]
    public void InvalidPosition_UsesPrimaryMonitorInsteadOfTheFirstEnumeratedMonitor()
    {
        Assert.Equal(new Point(1578d, 70d), OverlayWindowPlacement.KeepVisible(
            new Point(double.NaN, double.NaN), HudSize, [ThreeMonitors[2], ThreeMonitors[0]]));
    }

    [Fact]
    public void OversizedSettingsPanel_KeepsAnAccessibleArea()
    {
        var position = OverlayWindowPlacement.KeepVisible(
            new Point(1578d, 70d), new Size(318d, 1800d), [ThreeMonitors[0]]);
        Assert.Equal(new Point(1578d, 70d), position);
        Assert.True(Rect.Intersect(new Rect(position, new Size(318d, 1800d)), ThreeMonitors[0].WorkArea).Height >= 80d);
    }

    [Fact]
    public void ClosingAnUnshownOverlay_DoesNotOverwriteSavedSettings()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "IsleLiveMap-placement-tests", Guid.NewGuid().ToString("N"));
            try
            {
                var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.InitializeComponent();
                var path = Path.Combine(directory, "layout.json");
                var store = new OverlayLayoutSettingsStore(path);
                store.Save(new OverlayLayoutSettings { Left = 1578d, Top = 70d, MapZoom = 9.25d });
                var original = File.ReadAllText(path);
                var window = new MainWindow(guest: true, store, new PlayerPathTrailStore(Path.Combine(directory, "trail.json")));
                Assert.False(window.IsLoaded);
                window.Close();
                Assert.Equal(original, File.ReadAllText(path));
            }
            catch (Exception exception) { failure = exception; }
            finally
            {
                if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Unshown overlay did not close.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
