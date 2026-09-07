using System.IO;
using System.Xml.Linq;

namespace TheIsleOverlay.App.Tests;

public sealed class LargeMapWindowTests
{
    [Fact]
    public void LargeMap_SupportsDestinationSelectionAndCorrectLayerOrder()
    {
        var document = XDocument.Load(Path.Combine(
            AppContext.BaseDirectory,
            "TestAssets",
            "LargeMapWindow.xaml"));
        XName nameAttribute = "{http://schemas.microsoft.com/winfx/2006/xaml}Name";

        var window = document.Root!;
        Assert.Equal("NoResize", (string?)window.Attribute("ResizeMode"));
        Assert.Equal("Manual", (string?)window.Attribute("WindowStartupLocation"));
        Assert.Null(window.Attribute("AllowsTransparency"));
        var mapBorder = Named(document, nameAttribute, "MapBorder");
        Assert.Equal("16", (string?)mapBorder.Attribute("CornerRadius"));
        Assert.Equal("0", (string?)mapBorder.Attribute("BorderThickness"));

        var canvas = Named(document, nameAttribute, "LargeMapCanvas");
        Assert.Equal("1112", (string?)canvas.Attribute("Width"));
        Assert.Equal("1116", (string?)canvas.Attribute("Height"));
        Assert.Equal("MapCanvas_MouseLeftButtonDown", (string?)canvas.Attribute("MouseLeftButtonDown"));
        Assert.Equal("MapCanvas_MouseLeftButtonUp", (string?)canvas.Attribute("MouseLeftButtonUp"));
        Assert.Equal("MapCanvas_MouseMove", (string?)canvas.Attribute("MouseMove"));
        Assert.Equal("MapCanvas_MouseRightButtonDown", (string?)canvas.Attribute("MouseRightButtonDown"));

        var viewport = Named(document, nameAttribute, "MapViewport");
        Assert.Equal("MapViewport_MouseWheel", (string?)viewport.Attribute("MouseWheel"));
        Assert.Equal("ScaleTransform", Named(document, nameAttribute, "ZoomScaleTransform").Name.LocalName);
        Assert.Equal("TranslateTransform", Named(document, nameAttribute, "ZoomPanTransform").Name.LocalName);

        AssertLayer(document, nameAttribute, "LargeMapImage", "0", false);
        AssertLayer(document, nameAttribute, "LargeZoneLayer", "1", true);
        AssertLayer(document, nameAttribute, "LargePlayerLayer", "2", true);
        AssertLayer(document, nameAttribute, "LargeRouteLayer", "3", true);
        AssertLayer(document, nameAttribute, "LargeMarkerLayer", "4", true);

        Assert.DoesNotContain(document.Descendants(), element =>
            element.Name.LocalName is "Button" or "TextBlock");
    }

    private static XElement Named(XDocument document, XName nameAttribute, string name) =>
        Assert.Single(document.Descendants(), element =>
            string.Equals((string?)element.Attribute(nameAttribute), name, StringComparison.Ordinal));

    private static void AssertLayer(
        XDocument document,
        XName nameAttribute,
        string name,
        string zIndex,
        bool ignoresInput)
    {
        var layer = Named(document, nameAttribute, name);
        Assert.Equal(zIndex, (string?)layer.Attribute("Panel.ZIndex"));
        if (ignoresInput)
        {
            Assert.Equal("False", (string?)layer.Attribute("IsHitTestVisible"));
        }
    }
}
