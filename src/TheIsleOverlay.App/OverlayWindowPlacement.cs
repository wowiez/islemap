using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace TheIsleOverlay.App;

internal readonly record struct OverlayDisplayArea(Rect WorkArea, bool IsPrimary = false);

internal static class OverlayWindowPlacement
{
    public static IReadOnlyList<OverlayDisplayArea> ReadDisplayAreas(Visual visual)
    {
        var dpi = VisualTreeHelper.GetDpi(visual);
        var areas = new List<OverlayDisplayArea>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, ReadMonitor, IntPtr.Zero);
        return areas.Count > 0
            ? areas
            : [new OverlayDisplayArea(SystemParameters.WorkArea, IsPrimary: true)];

        bool ReadMonitor(IntPtr monitor, IntPtr dc, ref NativeRect bounds, IntPtr data)
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                areas.Add(new OverlayDisplayArea(new Rect(
                    info.Work.Left / dpi.DpiScaleX,
                    info.Work.Top / dpi.DpiScaleY,
                    (info.Work.Right - info.Work.Left) / dpi.DpiScaleX,
                    (info.Work.Bottom - info.Work.Top) / dpi.DpiScaleY),
                    IsPrimary: (info.Flags & 1u) != 0));
            }
            return true;
        }
    }

    public static Point DefaultPosition(Size size, IReadOnlyList<OverlayDisplayArea> displays)
    {
        var area = PrimaryArea(displays);
        return Fit(new Point(area.Right - size.Width - 24d, area.Top + 70d), size, area);
    }

    public static Point KeepVisible(Point position, Size size, IReadOnlyList<OverlayDisplayArea> displays)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y))
            return DefaultPosition(size, displays);

        var window = new Rect(position, size);
        var largestIntersection = 0d;
        Rect? selectedArea = null;
        foreach (var display in displays)
        {
            var intersection = Rect.Intersect(window, display.WorkArea);
            var area = intersection.IsEmpty ? 0d : intersection.Width * intersection.Height;
            if (area > largestIntersection)
            {
                largestIntersection = area;
                selectedArea = display.WorkArea;
            }
        }

        // The virtual desktop's bounding rectangle includes gaps between offset
        // monitors. A point inside that rectangle can still be completely invisible.
        return selectedArea is { } workArea
            ? Fit(position, size, workArea)
            : DefaultPosition(size, displays);
    }

    private static Rect PrimaryArea(IReadOnlyList<OverlayDisplayArea> displays)
    {
        foreach (var display in displays)
            if (display.IsPrimary) return display.WorkArea;
        return displays.Count > 0 ? displays[0].WorkArea : SystemParameters.WorkArea;
    }

    private static Point Fit(Point position, Size size, Rect area) => new(
        FitCoordinate(position.X, size.Width, area.Left, area.Width),
        FitCoordinate(position.Y, size.Height, area.Top, area.Height));

    private static double FitCoordinate(double position, double size, double start, double available)
    {
        if (size <= available) return Math.Clamp(position, start, start + available - size);
        var visible = Math.Min(80d, available);
        return Math.Clamp(position, start - size + visible, start + available - visible);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor, Work;
        public uint Flags;
    }

    private delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect bounds, IntPtr data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
