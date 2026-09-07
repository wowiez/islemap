using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TheIsleOverlay.App;

public static class DrinkingWaterOverlay
{
    private static readonly Lazy<BitmapSource> Cached = new(Load);
    public static BitmapSource Image => Cached.Value;

    private static BitmapSource Load()
    {
        var resource = Application.GetResourceStream(new Uri("/IsleLiveMap;component/Assets/GatewayDrinkingWater.webp", UriKind.Relative))
            ?? throw new InvalidOperationException("Missing drinking water overlay.");
        using var stream = resource.Stream;
        var source = new BitmapImage();
        source.BeginInit();
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.StreamSource = stream;
        source.EndInit();
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        IsolateWater(pixels);
        var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96,
            PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    // The source also contains dark, partially opaque pixels. Retain only
    // cyan water coverage, including antialiased edges, without darkening land.
    public static void IsolateWater(byte[] bgra)
    {
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            var cyan = Math.Min(bgra[i], bgra[i + 1]) - bgra[i + 2];
            var coverage = Math.Clamp((cyan - 24d) / 100d, 0d, 1d);
            bgra[i + 3] = (byte)Math.Round(bgra[i + 3] * coverage);
            bgra[i] = 238;
            bgra[i + 1] = 207;
            bgra[i + 2] = 52;
        }
    }
}
