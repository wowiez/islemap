using System.IO;
using System.Text.Json;

namespace TheIsleOverlay.App;

public sealed record OverlayLayoutSettings
{
    public int Version { get; init; } = 8;
    public double Scale { get; init; } = OverlayLayoutRules.DefaultScale;
    public double MapZoom { get; init; } = OverlayLayoutRules.DefaultMapZoom;
    public string MapStyle { get; init; } = OverlayLayoutRules.DefaultMapStyle;
    public bool ShowMap { get; init; } = true;
    public bool ShowActivity { get; init; } = true;
    public bool RotateMap { get; init; }
    public bool AutoDetectLiveMap { get; init; } = true;
    public bool ShowPrimeTasks { get; init; }
    public double? Left { get; init; }
    public double? Top { get; init; }
}

public static class OverlayLayoutRules
{
    public const double BaseWidth = 318d;
    public const double DefaultScale = 1d;
    public const double MinimumScale = 0.65d;
    public const double MaximumScale = 1.75d;
    public const double ButtonStep = 0.1d;
    public const double DefaultMapZoom = 2.25d;
    public const double MinimumMapZoom = 1d;
    public const double MaximumMapZoom = 6d;
    public const string DefaultMapStyle = "circle";
    public const string SquareMapStyle = "square";

    public static OverlayLayoutSettings Normalize(OverlayLayoutSettings? settings)
    {
        settings ??= new OverlayLayoutSettings();
        return settings with
        {
            Version = 8,
            Scale = NormalizeScale(settings.Scale),
            MapZoom = NormalizeMapZoom(settings.MapZoom),
            MapStyle = NormalizeMapStyle(settings.MapStyle),
            ShowMap = settings.ShowMap || !settings.ShowActivity,
            ShowActivity = settings.ShowActivity,
            RotateMap = settings.RotateMap,
            AutoDetectLiveMap = settings.AutoDetectLiveMap,
            ShowPrimeTasks = settings.ShowPrimeTasks,
            Left = FiniteOrNull(settings.Left),
            Top = FiniteOrNull(settings.Top)
        };
    }

    public static double NormalizeScale(double scale)
    {
        if (!double.IsFinite(scale))
        {
            return DefaultScale;
        }

        var clamped = Math.Clamp(scale, MinimumScale, MaximumScale);
        return Math.Round(clamped * 20d, MidpointRounding.AwayFromZero) / 20d;
    }

    public static double PixelAlignedScale(double scale, double dpiScale)
    {
        var normalized = NormalizeScale(scale);
        var dpi = double.IsFinite(dpiScale) && dpiScale > 0d ? dpiScale : 1d;
        return Math.Round(BaseWidth * normalized * dpi, MidpointRounding.AwayFromZero)
               / (BaseWidth * dpi);
    }

    public static double ScaleFromHorizontalDrag(double startingScale, double deltaDip) =>
        NormalizeScale(startingScale + deltaDip / BaseWidth);

    public static string FormatScale(double scale) => $"{NormalizeScale(scale) * 100d:0}%";

    public static double NormalizeMapZoom(double zoom)
    {
        if (!double.IsFinite(zoom))
        {
            return DefaultMapZoom;
        }

        return Math.Round(
            Math.Clamp(zoom, MinimumMapZoom, MaximumMapZoom),
            2,
            MidpointRounding.AwayFromZero);
    }

    public static string FormatMapZoom(double zoom) => $"{NormalizeMapZoom(zoom) * 100d:0}%";

    public static string NormalizeMapStyle(string? style) =>
        string.Equals(style, SquareMapStyle, StringComparison.OrdinalIgnoreCase)
            ? SquareMapStyle
            : DefaultMapStyle;

    private static double? FiniteOrNull(double? value) =>
        value is { } number && double.IsFinite(number) ? number : null;
}

public sealed class OverlayLayoutSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _path;

    public OverlayLayoutSettingsStore(string? path = null)
    {
        var overridePath = Environment.GetEnvironmentVariable(
            "ISLELIVEMAP_LAYOUT_SETTINGS_PATH");
        _path = string.IsNullOrWhiteSpace(path)
            ? string.IsNullOrWhiteSpace(overridePath)
                ? AppPaths.OverlayLayoutSettings
                : Path.GetFullPath(overridePath)
            : Path.GetFullPath(path);
    }

    public OverlayLayoutSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new OverlayLayoutSettings();
            }

            var settings = JsonSerializer.Deserialize<OverlayLayoutSettings>(
                File.ReadAllText(_path),
                JsonOptions);
            return OverlayLayoutRules.Normalize(settings);
        }
        catch
        {
            return new OverlayLayoutSettings();
        }
    }

    public void Save(OverlayLayoutSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Overlay settings path has no parent directory.");
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(_path)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(OverlayLayoutRules.Normalize(settings), JsonOptions));
            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        catch
        {
            // Layout changes remain usable even if Windows temporarily blocks persistence.
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch
                {
                }
            }
        }
    }
}
