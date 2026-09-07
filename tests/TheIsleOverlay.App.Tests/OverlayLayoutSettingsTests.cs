using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class OverlayLayoutSettingsTests
{
    [Theory]
    [InlineData(double.NaN, 1d)]
    [InlineData(double.NegativeInfinity, 1d)]
    [InlineData(0.2d, 0.65d)]
    [InlineData(0.45d, 0.65d)]
    [InlineData(0.65d, 0.65d)]
    [InlineData(1.234d, 1.25d)]
    [InlineData(1.75d, 1.75d)]
    [InlineData(4d, 1.75d)]
    public void Scale_IsFiniteRoundedAndClamped(double input, double expected)
    {
        Assert.Equal(expected, OverlayLayoutRules.NormalizeScale(input));
    }

    [Fact]
    public void HorizontalDrag_ResizesAgainstTheWholeBaseOverlayWidth()
    {
        Assert.Equal(
            1.5d,
            OverlayLayoutRules.ScaleFromHorizontalDrag(
                startingScale: 1d,
                deltaDip: OverlayLayoutRules.BaseWidth / 2d));
        Assert.Equal(
            OverlayLayoutRules.MaximumScale,
            OverlayLayoutRules.ScaleFromHorizontalDrag(1.7d, 500d));
        Assert.Equal("65%", OverlayLayoutRules.FormatScale(0.2d));
    }

    [Fact]
    public void Scale_AlignsTheHudWidthToPhysicalPixels()
    {
        var scale = OverlayLayoutRules.PixelAlignedScale(0.65d, 1d);

        Assert.Equal(Math.Round(OverlayLayoutRules.BaseWidth * 0.65d), OverlayLayoutRules.BaseWidth * scale, precision: 8);
        Assert.Equal(1d, OverlayLayoutRules.PixelAlignedScale(1d, double.NaN), precision: 8);
    }

    [Theory]
    [InlineData(double.NaN, 2.25d)]
    [InlineData(0.2d, 1d)]
    [InlineData(2.347d, 2.35d)]
    [InlineData(9d, 6d)]
    public void MapZoom_IsFiniteRoundedAndClamped(double input, double expected)
    {
        Assert.Equal(expected, OverlayLayoutRules.NormalizeMapZoom(input));
        Assert.Equal($"{expected * 100d:0}%", OverlayLayoutRules.FormatMapZoom(input));
    }

    [Theory]
    [InlineData(null, "circle")]
    [InlineData("circle", "circle")]
    [InlineData("SQUARE", "square")]
    [InlineData("unknown", "circle")]
    public void MapStyle_AllowsOnlyCircleOrSquare(string? input, string expected) =>
        Assert.Equal(expected, OverlayLayoutRules.NormalizeMapStyle(input));

    [Fact]
    public void Store_RoundTripsScaleAndPositionAndRecoversFromMalformedJson()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "IsleLiveMap.Tests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "overlay-layout.json");
        try
        {
            var store = new OverlayLayoutSettingsStore(path);
            Assert.Equal(new OverlayLayoutSettings(), store.Load());
            Assert.False(store.Load().RotateMap);
            Assert.False(store.Load().ShowPrimeTasks);

            store.Save(new OverlayLayoutSettings
            {
                Scale = 1.35d,
                MapZoom = 3.1d,
                MapStyle = "SQUARE",
                ShowMap = false,
                ShowActivity = true,
                RotateMap = true,
                AutoDetectLiveMap = false,
                ShowPrimeTasks = false,
                Left = 120.5d,
                Top = 80.25d
            });
            var restored = store.Load();
            Assert.Equal(1.35d, restored.Scale);
            Assert.Equal(3.1d, restored.MapZoom);
            Assert.Equal(OverlayLayoutRules.SquareMapStyle, restored.MapStyle);
            Assert.False(restored.ShowMap);
            Assert.True(restored.ShowActivity);
            Assert.True(restored.RotateMap);
            Assert.False(restored.AutoDetectLiveMap);
            Assert.False(restored.ShowPrimeTasks);
            Assert.Equal(120.5d, restored.Left);
            Assert.Equal(80.25d, restored.Top);

            File.WriteAllText(path, "{broken");
            Assert.Equal(new OverlayLayoutSettings(), store.Load());
            Assert.False(store.Load().RotateMap);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Visibility_AlwaysKeepsAtLeastOnePrimaryPanel()
    {
        var normalized = OverlayLayoutRules.Normalize(new OverlayLayoutSettings
        {
            ShowMap = false,
            ShowActivity = false
        });

        Assert.True(normalized.ShowMap);
        Assert.False(normalized.ShowActivity);
    }

    [Fact]
    public void Overlay_ExposesOneSettingsPanelWithScaleCropAndDragControls()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "MainWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        XElement Control(string name) => Assert.Single(
            document.Descendants(),
            element => string.Equals(
                (string?)element.Attribute(nameAttribute),
                name,
                StringComparison.Ordinal));

        var window = document.Root ?? throw new InvalidOperationException("Window root is missing.");
        Assert.Equal("False", (string?)window.Attribute("ShowActivated"));
        Assert.Equal("WidthAndHeight", (string?)window.Attribute("SizeToContent"));
        Assert.Null(window.Attribute("Width"));
        Assert.Equal("318", (string?)Control("OverlayScaleRoot").Attribute("Width"));
        Assert.NotNull(Control("OverlayScaleTransform"));
        var settings = Control("SettingsPanel");
        Assert.Equal("Collapsed", (string?)settings.Attribute("Visibility"));
        Assert.NotNull(Control("SettingsReadabilityScaleTransform"));
        Assert.NotNull(Control("ActivityReadabilityScaleTransform"));
        Assert.NotNull(Control("OverlayScaleSlider"));
        Assert.NotNull(Control("MapZoomSlider"));
        Assert.NotNull(Control("CircleStyleButton"));
        Assert.NotNull(Control("SquareStyleButton"));
        Assert.Equal("0", (string?)Control("CircularMapOuterFrame").Attribute("StrokeThickness"));
        Assert.Equal("0", (string?)Control("CircularMapInnerFrame").Attribute("StrokeThickness"));
        Assert.Equal("0", (string?)Control("SquareMapFrame").Attribute("BorderThickness"));
        Assert.NotNull(Control("MapVisibilityButton"));
        Assert.NotNull(Control("ActivityVisibilityButton"));
        Assert.NotNull(Control("MapRotationButton"));
        Assert.NotNull(Control("AutoDetectLiveMapButton"));
        Assert.NotNull(Control("PrimeTasksVisibilityButton"));
        var primePanel = Control("PrimeTasksPanel");
        var activityPanel = Control("ActivityPanel");
        Assert.Equal((string?)activityPanel.Attribute("Width"), (string?)primePanel.Attribute("Width"));
        Assert.NotNull(Control("PrimeTasksReadabilityScaleTransform"));
        Assert.DoesNotContain(document.Descendants(), element =>
            ((string?)element.Attribute(nameAttribute))?.Contains("Team", StringComparison.Ordinal) == true);
        Assert.NotNull(Control("DragRegion"));
        var resizeGrip = Control("ResizeGrip");
        Assert.Equal("Thumb", resizeGrip.Name.LocalName);
        Assert.Equal("ResizeGrip_DragStarted", (string?)resizeGrip.Attribute("DragStarted"));
        Assert.Equal("ResizeGrip_DragDelta", (string?)resizeGrip.Attribute("DragDelta"));
        Assert.Equal("ResizeGrip_DragCompleted", (string?)resizeGrip.Attribute("DragCompleted"));
        Assert.Equal("100%", (string?)Control("OverlayScaleLabel").Attribute("Text"));
        Assert.Equal("225%", (string?)Control("MapZoomLabel").Attribute("Text"));
        Assert.DoesNotContain(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "HotkeyGuide", StringComparison.Ordinal));
    }

    [Fact]
    public void Overlay_UsesCircularHeadingUpMap()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        var viewport = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "MapViewport", StringComparison.Ordinal));
        Assert.NotNull(viewport.Element(presentation + "Canvas.Clip")?.Element(presentation + "EllipseGeometry"));
        Assert.Equal("True", (string?)document.Root?.Attribute("UseLayoutRounding"));
        Assert.Equal("True", (string?)document.Root?.Attribute("SnapsToDevicePixels"));

        var rotation = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "MapRotationTransform", StringComparison.Ordinal));
        Assert.Equal("RotateTransform", rotation.Name.LocalName);
        Assert.NotNull(document.Descendants().Single(element =>
            string.Equals((string?)element.Attribute(nameAttribute), "CompassRingRotationTransform", StringComparison.Ordinal)));
        var compassRing = document.Descendants().Single(element =>
            string.Equals((string?)element.Attribute(nameAttribute), "CompassRing", StringComparison.Ordinal));
        Assert.Equal("272", (string?)compassRing.Attribute("Width"));
        Assert.Equal("272", (string?)compassRing.Attribute("Height"));
        foreach (var labelName in new[]
                 {
                     "CompassNorthLabel",
                     "CompassEastLabel",
                     "CompassSouthLabel",
                     "CompassWestLabel"
                 })
        {
            var label = document.Descendants().Single(element =>
                string.Equals((string?)element.Attribute(nameAttribute), labelName, StringComparison.Ordinal));
            Assert.Null(label.Attribute("Background"));
            Assert.Null(label.Attribute("Padding"));
            Assert.Equal("Segoe UI", (string?)label.Attribute("FontFamily"));
            Assert.Equal("Condensed", (string?)label.Attribute("FontStretch"));
            Assert.Equal("Bold", (string?)label.Attribute("FontWeight"));
            Assert.Equal("11.5", (string?)label.Attribute("FontSize"));
            var effect = label.Element(presentation + "TextBlock.Effect")?.Element(presentation + "DropShadowEffect");
            Assert.NotNull(effect);
            Assert.Equal("#000000", (string?)effect.Attribute("Color"));
            Assert.Equal("0", (string?)effect.Attribute("ShadowDepth"));
        }
        var headingReadout = document.Descendants().Single(element =>
            string.Equals((string?)element.Attribute(nameAttribute), "HeadingReadout", StringComparison.Ordinal));
        Assert.Equal("Collapsed", (string?)headingReadout.Attribute("Visibility"));
        Assert.Equal(4, document.Descendants().Count(element =>
            ((string?)element.Attribute(nameAttribute))?.EndsWith("LabelCounterRotationTransform", StringComparison.Ordinal) == true));

        var marker = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "DirectionNeedle", StringComparison.Ordinal));
        Assert.NotNull(marker.Element(presentation + "Grid.RenderTransform")?.Element(presentation + "RotateTransform"));
        Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "SbtcZoneLayer", StringComparison.Ordinal));
        var zoneDecorationLayer = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "SbtcZoneDecorationLayer", StringComparison.Ordinal));
        var routeLayer = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "RouteLayer", StringComparison.Ordinal));
        var playerLayer = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "SbtcPlayerLayer", StringComparison.Ordinal));
        var playerMarker = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "PlayerMarker", StringComparison.Ordinal));
        Assert.Equal("3", (string?)zoneDecorationLayer.Attribute("Panel.ZIndex"));
        Assert.Equal("4", (string?)routeLayer.Attribute("Panel.ZIndex"));
        Assert.Equal("5", (string?)playerLayer.Attribute("Panel.ZIndex"));
        Assert.Equal("6", (string?)playerMarker.Attribute("Panel.ZIndex"));
        Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "SbtcZoneRotationTransform", StringComparison.Ordinal));
        Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "SbtcZoneDecorationRotationTransform", StringComparison.Ordinal));
        Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "RouteRotationTransform", StringComparison.Ordinal));
        foreach (var panTransformName in new[]
                 {
                     "MapImagePanTransform",
                     "SbtcZonePanTransform",
                     "SbtcZoneDecorationPanTransform",
                     "RoutePanTransform",
                     "SbtcPlayerPanTransform"
                 })
        {
            var panTransform = Assert.Single(document.Descendants(), element =>
                string.Equals((string?)element.Attribute(nameAttribute), panTransformName, StringComparison.Ordinal));
            Assert.Equal("TranslateTransform", panTransform.Name.LocalName);
        }
        Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "SquareMapFrame", StringComparison.Ordinal));
    }

    [Fact]
    public void Overlay_UsesTranslucentHudWithoutPanelDropShadows()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";
        XName keyAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Key";

        var hudBase = Assert.Single(document.Descendants(presentation + "SolidColorBrush"), element =>
            string.Equals((string?)element.Attribute(keyAttribute), "HudPatternBrush", StringComparison.Ordinal));
        Assert.Equal("#A8080D0C", (string?)hudBase.Attribute("Color"));
        Assert.Empty(document.Descendants(presentation + "DrawingBrush"));

        foreach (var panelName in new[] { "MapPanel", "ActivityPanel" })
        {
            var panel = Assert.Single(document.Descendants(), element =>
                string.Equals((string?)element.Attribute(nameAttribute), panelName, StringComparison.Ordinal));
            Assert.Null(panel.Element(presentation + "Border.Effect"));
        }
    }

    [Fact]
    public void Activity_UsesVectorSteakForFoodStatus()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        var steak = Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), "FoodSteakIcon", StringComparison.Ordinal));
        Assert.Equal("Thức ăn", (string?)steak.Attribute("ToolTip"));
        var meat = Assert.Single(steak.Descendants(presentation + "Path"));
        Assert.Equal("#E49428", (string?)meat.Attribute("Fill"));
        Assert.Empty(steak.Descendants(presentation + "Ellipse"));
        Assert.Empty(steak.Descendants(presentation + "TextBlock"));
    }
}
