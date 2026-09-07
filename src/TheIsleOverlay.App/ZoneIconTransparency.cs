using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TheIsleOverlay.App;

public static class ZoneIconTransparency
{
    private const int BackgroundTolerance = 24;

    public static BitmapSource CutEdgeBackground(BitmapSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0d);
        var width = converted.PixelWidth;
        var height = converted.PixelHeight;
        var stride = checked(width * 4);
        var pixels = new byte[checked(stride * height)];
        converted.CopyPixels(pixels, stride, 0);

        // A correct WebP alpha channel needs no color-keying. Rebuilding as
        // BGRA still makes alpha handling deterministic across WIC codecs.
        if (!pixels.Where((_, index) => index % 4 == 3).All(alpha => alpha == byte.MaxValue))
        {
            return CreateBitmap(source, pixels, stride);
        }

        CutOpaqueBackgroundConnectedToEdges(pixels, width, height, stride);
        return CreateBitmap(source, pixels, stride);
    }

    public static void CutOpaqueBackgroundConnectedToEdges(
        byte[] pixels,
        int width,
        int height,
        int stride)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0 || stride < width * 4 || pixels.Length < stride * height)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid BGRA bitmap dimensions.");
        }

        var referenceBlue = pixels[0];
        var referenceGreen = pixels[1];
        var referenceRed = pixels[2];
        var visited = new bool[checked(width * height)];
        var queue = new Queue<int>();

        void EnqueueIfBackground(int x, int y)
        {
            var pixelIndex = y * width + x;
            if (visited[pixelIndex])
            {
                return;
            }

            visited[pixelIndex] = true;
            var offset = y * stride + x * 4;
            if (Math.Abs(pixels[offset] - referenceBlue) <= BackgroundTolerance &&
                Math.Abs(pixels[offset + 1] - referenceGreen) <= BackgroundTolerance &&
                Math.Abs(pixels[offset + 2] - referenceRed) <= BackgroundTolerance)
            {
                queue.Enqueue(pixelIndex);
            }
        }

        for (var x = 0; x < width; x++)
        {
            EnqueueIfBackground(x, 0);
            EnqueueIfBackground(x, height - 1);
        }

        for (var y = 1; y < height - 1; y++)
        {
            EnqueueIfBackground(0, y);
            EnqueueIfBackground(width - 1, y);
        }

        while (queue.TryDequeue(out var pixelIndex))
        {
            var x = pixelIndex % width;
            var y = pixelIndex / width;
            pixels[y * stride + x * 4 + 3] = 0;

            if (x > 0) EnqueueIfBackground(x - 1, y);
            if (x + 1 < width) EnqueueIfBackground(x + 1, y);
            if (y > 0) EnqueueIfBackground(x, y - 1);
            if (y + 1 < height) EnqueueIfBackground(x, y + 1);
        }
    }

    private static BitmapSource CreateBitmap(BitmapSource source, byte[] pixels, int stride)
    {
        var bitmap = BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX,
            source.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        bitmap.Freeze();
        return bitmap;
    }
}
