using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TheIsleOverlay.App;

internal static class SbtcWildlifeMarker
{
    private static readonly Lazy<IconCatalogue> Catalogue = new(LoadCatalogue);
    private static readonly Dictionary<string, ImageSource> Images = new(StringComparer.Ordinal);
    private static readonly object ImageLock = new();

    public static Image Create(string species, string? color, double size) => new()
    {
        Width = size, Height = size, Source = ImageFor(species, color), Stretch = Stretch.Uniform,
        ToolTip = $"AI · {species}"
    };

    internal static double LargeMapSize(double zoom, double fitScale)
    {
        zoom = double.IsFinite(zoom) ? Math.Clamp(zoom, 1d, LargeMapWindow.MaximumZoom) : 1d;
        fitScale = double.IsFinite(fitScale) && fitScale > 0d ? Math.Clamp(fitScale, 0.05d, 4d) : 1d;
        // Compensate both transforms: the Viewbox fits the map into the window,
        // then the wheel zoom magnifies it. Keep the island overview compact
        // while allowing larger, readable icons as the player zooms closer.
        var screenSize = 18d + 10d * Math.Log2(zoom);
        return screenSize / (zoom * fitScale);
    }

    internal static ImageSource ImageFor(string species, string? color)
    {
        var normalized = new string(species.Take(80).Where(char.IsAsciiLetter).Select(char.ToLowerInvariant).ToArray());
        var catalogue = Catalogue.Value;
        var alias = catalogue.SpeciesAliases.FirstOrDefault(pair => pair.Count == 2 && normalized.Contains(pair[0], StringComparison.Ordinal));
        var name = alias?[1] ?? string.Empty;
        var key = name.Length > 0 ? name : $"fallback:{color}";
        lock (ImageLock)
        {
            if (Images.TryGetValue(key, out var existing)) return existing;
            var drawing = new DrawingGroup();
            drawing.Children.Add(new GeometryDrawing(Brushes.Transparent, null,
                new RectangleGeometry(new Rect(-1d, -1d, 26d, 26d))));
            if (catalogue.Art.TryGetValue(name, out var parts))
            {
                foreach (var part in parts)
                {
                    var geometry = Geometry.Parse("F1 " + part.Path);
                    if (part.Outline)
                        drawing.Children.Add(new GeometryDrawing(null,
                            new Pen(BrushFrom(catalogue.OutlineColor), 1.5d) { LineJoin = PenLineJoin.Round }, geometry));
                    drawing.Children.Add(new GeometryDrawing(BrushFrom(part.Fill), null, geometry));
                }
            }
            else
            {
                // Unknown species still get an animal marker and their server colour.
                var paw = Geometry.Parse("M12 21.8c-2.8 0-5.1-1.4-5.1-3.9 0-3.2 3.2-6.5 5.1-6.5s5.1 3.3 5.1 6.5c0 2.5-2.3 3.9-5.1 3.9Z M5.2 11.2a2.7 3.3 0 1 0 0-6.6 2.7 3.3 0 1 0 0 6.6Z M18.8 11.2a2.7 3.3 0 1 0 0-6.6 2.7 3.3 0 1 0 0 6.6Z M12 9.6a2.9 3.5 0 1 0 0-7 2.9 3.5 0 1 0 0 7Z");
                drawing.Children.Add(new GeometryDrawing(BrushFrom(color), new Pen(Brushes.Black, 1d), paw));
            }
            drawing.Freeze();
            var image = new DrawingImage(drawing);
            image.Freeze();
            Images[key] = image;
            return image;
        }
    }

    private static Brush BrushFrom(string? color)
    {
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color ?? "#8A8A8A")); }
        catch (Exception exception) when (exception is FormatException or NotSupportedException) { return Brushes.Gray; }
    }

    private static IconCatalogue LoadCatalogue()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri(
                "/IsleLiveMap;component/Assets/SbtcWildlifeIcons.json", UriKind.Relative));
            if (resource is null) return new();
            using var stream = resource.Stream;
            return JsonSerializer.Deserialize<IconCatalogue>(stream, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? new();
        }
        catch (Exception exception) when (exception is IOException or JsonException) { return new(); }
    }

    private sealed record IconCatalogue
    {
        public string OutlineColor { get; init; } = "#1A130C";
        public IReadOnlyList<IReadOnlyList<string>> SpeciesAliases { get; init; } = [];
        public Dictionary<string, IReadOnlyList<IconPart>> Art { get; init; } = [];
    }

    private sealed record IconPart(string Path, string Fill, bool Outline);
}
