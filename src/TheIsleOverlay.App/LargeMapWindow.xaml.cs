using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using TheIsleOverlay.Core;

namespace TheIsleOverlay.App;

public partial class LargeMapWindow : Window
{
    private static readonly Uri GatewayMapResourceUri = new("Assets/GatewayMap.webp", UriKind.Relative);
    private const double MapWidth = 1112d;
    private const double MapHeight = 1116d;
    private const double MinimumZoom = 1d;
    private const double MaximumZoom = 4d;
    private const double ZoomStep = 0.25d;
    private static readonly Duration ZoomAnimationDuration = new(TimeSpan.FromMilliseconds(180));

    private MapPoint? _currentLocation;
    private MapPoint? _renderedCurrentLocation;
    private MapPoint? _destination;
    private IReadOnlyList<SbtcZoneFeature> _zones = [];
    private IReadOnlyList<SbtcPlayerMarker> _players = [];
    private Point? _dragStart;
    private Vector _dragStartPan;
    private bool _dragMoved;
    private double _zoom = MinimumZoom;
    private bool _allowClose;

    public LargeMapWindow(ImageSource? sharedMapSource = null)
    {
        InitializeComponent();
        if (sharedMapSource is not null)
        {
            LargeMapImage.Source = sharedMapSource;
            LargeDrinkingWaterImage.Source = DrinkingWaterOverlay.Image;
        }
        else
        {
            LoadMap();
        }
    }

    public event Action<MapPoint?>? DestinationChanged;

    public void UpdateState(
        MapPoint? currentLocation,
        MapPoint? destination,
        IReadOnlyList<SbtcZoneFeature> zones,
        IReadOnlyList<SbtcPlayerMarker>? players = null)
    {
        _currentLocation = currentLocation;
        _destination = destination;
        _zones = zones ?? [];
        _players = players ?? [];
        RenderZones();
        RenderPlayers();
        RenderRouteAndMarkers();
    }

    public void UpdateCurrentLocation(MapPoint? currentLocation)
    {
        _currentLocation = currentLocation;
        RenderPlayers();
        RenderRouteAndMarkers();
    }

    public void UpdateZones(IReadOnlyList<SbtcZoneFeature> zones)
    {
        _zones = zones ?? [];
        RenderZones();
    }

    public void UpdateDestination(MapPoint? destination)
    {
        _destination = destination;
        RenderRouteAndMarkers();
    }

    public void UpdatePlayers(IReadOnlyList<SbtcPlayerMarker> players)
    {
        _players = players ?? [];
        RenderPlayers();
    }

    public bool PasteDestinationFromClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText(TextDataFormat.UnicodeText) ||
                !ClipboardRouteDestination.TryParse(
                    Clipboard.GetText(TextDataFormat.UnicodeText),
                    out var destination))
            {
                return false;
            }

            _destination = destination;
            RenderRouteAndMarkers();
            DestinationChanged?.Invoke(destination);
            return true;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    public void ShowCentered()
    {
        CenterOnOwnerScreen();
        if (!IsVisible)
        {
            Show();
        }

        CenterOnOwnerScreen();
        Activate();
        PlayOpenAnimation();
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    private void LoadMap()
    {
        try
        {
            var resource = Application.GetResourceStream(GatewayMapResourceUri)
                ?? throw new InvalidOperationException("Gateway map resource is missing.");
            using var stream = resource.Stream;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            LargeMapImage.Source = image;
            LargeDrinkingWaterImage.Source = DrinkingWaterOverlay.Image;
        }
        catch
        {
            LargeMapCanvas.Background = BrushFrom("#18221F");
        }
    }

    private void RenderZones()
    {
        LargeZoneLayer.Children.Clear();
        foreach (var feature in _zones)
        {
            var points = new PointCollection(feature.Points.Select(ToCanvasPoint));
            if (points.Count == 0)
            {
                continue;
            }

            var stroke = ZoneBrush(feature, 0xE8);
            var fill = ZoneBrush(feature, 0x42);
            var isPolygon = string.Equals(feature.Shape, "polygon", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(feature.Shape) && points.Count >= 3;
            if (isPolygon && points.Count >= 3)
            {
                LargeZoneLayer.Children.Add(new Polygon
                {
                    Points = points,
                    Stroke = stroke,
                    Fill = fill,
                    StrokeThickness = 2.2d,
                    StrokeLineJoin = PenLineJoin.Round
                });
            }
            else
            {
                var center = new Point(points.Average(point => point.X), points.Average(point => point.Y));
                var radius = Math.Max(7d, (feature.Size ?? 0.01d) * MapWidth);
                var circle = new Ellipse
                {
                    Width = radius * 2d,
                    Height = radius * 2d,
                    Stroke = stroke,
                    Fill = fill,
                    StrokeThickness = 2d
                };
                Canvas.SetLeft(circle, center.X - radius);
                Canvas.SetTop(circle, center.Y - radius);
                LargeZoneLayer.Children.Add(circle);
            }
        }

        foreach (var zoneLabel in SbtcZoneOverlay.CreateLabels(_zones))
        {
            var center = ToCanvasPoint(zoneLabel.Center);
            var accent = ZoneBrush(zoneLabel.Kind, 0xFF);
            var label = new Border
            {
                Background = BrushFrom("#C70A1110"),
                BorderBrush = accent,
                BorderThickness = new Thickness(0d, 0d, 0d, 2d),
                CornerRadius = new CornerRadius(4d),
                Padding = new Thickness(6d, 2d, 6d, 2d),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = zoneLabel.Name,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
                    FontSize = 14d,
                    FontWeight = FontWeights.SemiBold
                }
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var isPolygon = string.Equals(zoneLabel.Shape, "polygon", StringComparison.OrdinalIgnoreCase) ||
                            string.IsNullOrWhiteSpace(zoneLabel.Shape);
            var labelCenterY = isPolygon
                ? center.Y
                : center.Y - Math.Max(7d, (zoneLabel.Size ?? 0.01d) * MapWidth) - 15d;
            Canvas.SetLeft(label, center.X - label.DesiredSize.Width / 2d);
            Canvas.SetTop(label, labelCenterY - label.DesiredSize.Height / 2d);
            LargeZoneLayer.Children.Add(label);
        }
    }

    private void RenderRouteAndMarkers()
    {
        LargeRouteLayer.Children.Clear();
        LargeMarkerLayer.Children.Clear();

        if (_currentLocation is { } current)
        {
            var currentPoint = ToCanvasPoint(current);
            var currentMarker = AddMarker(currentPoint, "#FF2E88", 16d);
            if (_renderedCurrentLocation is { } previous && previous != current)
            {
                var previousPoint = ToCanvasPoint(previous);
                AnimateValue(
                    currentMarker,
                    Canvas.LeftProperty,
                    previousPoint.X - currentMarker.Width / 2d,
                    currentPoint.X - currentMarker.Width / 2d,
                    220d);
                AnimateValue(
                    currentMarker,
                    Canvas.TopProperty,
                    previousPoint.Y - currentMarker.Height / 2d,
                    currentPoint.Y - currentMarker.Height / 2d,
                    220d);
            }
        }

        if (_destination is not { } destination)
        {
            _renderedCurrentLocation = _currentLocation;
            return;
        }

        var destinationPoint = ToCanvasPoint(destination);
        var destinationMarker = AddMarker(destinationPoint, "#37D4C6", 20d);
        if (_currentLocation is { } routeStart)
        {
            var startPoint = ToCanvasPoint(routeStart);
            var outline = RouteLine(startPoint, destinationPoint, Brushes.Black, 9d, 0.72d);
            var route = RouteLine(
                startPoint,
                destinationPoint,
                BrushFrom("#37D4C6"),
                4.2d,
                1d);
            route.Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(55, 212, 198),
                BlurRadius = 12d,
                ShadowDepth = 0d,
                Opacity = 1d
            };
            LargeRouteLayer.Children.Add(outline);
            LargeRouteLayer.Children.Add(route);
            var distanceLabel = CreateLabel(
                MapOverlayPresentation.Distance(routeStart, destination),
                BrushFrom("#37D4C6"),
                14d);
            distanceLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(distanceLabel, destinationPoint.X - distanceLabel.DesiredSize.Width / 2d);
            Canvas.SetTop(
                distanceLabel,
                destinationPoint.Y - destinationMarker.Height / 2d - distanceLabel.DesiredSize.Height - 7d);
            LargeMarkerLayer.Children.Add(distanceLabel);
            if (_renderedCurrentLocation is { } previous && previous != routeStart)
            {
                var previousPoint = ToCanvasPoint(previous);
                foreach (var line in new[] { outline, route })
                {
                    AnimateValue(line, Line.X1Property, previousPoint.X, startPoint.X, 220d);
                    AnimateValue(line, Line.Y1Property, previousPoint.Y, startPoint.Y, 220d);
                }
            }
        }

        _renderedCurrentLocation = _currentLocation;
    }

    private void RenderPlayers()
    {
        LargePlayerLayer.Children.Clear();
        foreach (var player in _players)
        {
            var center = ToCanvasPoint(player.Location);
            var color = player.Group ? BrushFrom("#E879F9") : BrushFrom("#34D399");
            FrameworkElement marker;
            if (player.HeadingDegrees is { } heading)
            {
                marker = new Polygon
                {
                    Width = 19d,
                    Height = 19d,
                    Points = [new Point(9.5d, 0d), new Point(17d, 17d), new Point(9.5d, 13d), new Point(2d, 17d)],
                    Fill = color,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.8d,
                    Stretch = Stretch.None,
                    RenderTransformOrigin = new Point(0.5d, 0.5d),
                    RenderTransform = new RotateTransform(MapHeading.Normalize(heading))
                };
            }
            else
            {
                marker = new Ellipse
                {
                    Width = 14d,
                    Height = 14d,
                    Fill = color,
                    Stroke = Brushes.Black,
                    StrokeThickness = 1.8d
                };
            }

            marker.IsHitTestVisible = false;
            Canvas.SetLeft(marker, center.X - marker.Width / 2d);
            Canvas.SetTop(marker, center.Y - marker.Height / 2d);
            LargePlayerLayer.Children.Add(marker);

            var label = new Border
            {
                Background = BrushFrom("#D20A1110"),
                BorderBrush = color,
                BorderThickness = new Thickness(1d),
                CornerRadius = new CornerRadius(4d),
                Padding = new Thickness(5d, 2d, 5d, 2d),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = MapOverlayPresentation.PlayerLabel(
                        player.Label,
                        player.Location,
                        _currentLocation),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
                    FontSize = 13d,
                    FontWeight = FontWeights.SemiBold,
                    MaxWidth = 150d,
                    TextTrimming = TextTrimming.CharacterEllipsis
                }
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, center.X - label.DesiredSize.Width / 2d);
            Canvas.SetTop(label, center.Y - marker.Height / 2d - label.DesiredSize.Height - 5d);
            LargePlayerLayer.Children.Add(label);
        }
    }

    private static Border CreateLabel(string text, Brush accent, double fontSize) => new()
    {
        Background = BrushFrom("#E60A1110"),
        BorderBrush = accent,
        BorderThickness = new Thickness(1d),
        CornerRadius = new CornerRadius(4d),
        Padding = new Thickness(5d, 2d, 5d, 2d),
        IsHitTestVisible = false,
        Child = new TextBlock
        {
            Text = text,
            Foreground = Brushes.White,
            FontFamily = new FontFamily("Bahnschrift SemiCondensed"),
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold
        }
    };

    private Ellipse AddMarker(Point center, string color, double diameter)
    {
        var marker = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = BrushFrom(color),
            Stroke = Brushes.White,
            StrokeThickness = 3d
        };
        Canvas.SetLeft(marker, center.X - diameter / 2d);
        Canvas.SetTop(marker, center.Y - diameter / 2d);
        LargeMarkerLayer.Children.Add(marker);
        return marker;
    }

    private static Line RouteLine(Point start, Point end, Brush stroke, double thickness, double opacity) => new()
    {
        X1 = start.X,
        Y1 = start.Y,
        X2 = end.X,
        Y2 = end.Y,
        Stroke = stroke,
        StrokeThickness = thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        Opacity = opacity
    };

    private void MapCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(MapViewport);
        _dragStartPan = new Vector(ZoomPanTransform.X, ZoomPanTransform.Y);
        _dragMoved = false;
        LargeMapCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void MapCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(MapViewport);
        var delta = current - start;
        if (!_dragMoved && delta.Length < SystemParameters.MinimumHorizontalDragDistance)
        {
            return;
        }

        _dragMoved = true;
        LargeMapCanvas.Cursor = Cursors.Hand;
        SetPan(_dragStartPan.X + delta.X, _dragStartPan.Y + delta.Y, animate: false);
        e.Handled = true;
    }

    private void MapCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null)
        {
            return;
        }

        LargeMapCanvas.ReleaseMouseCapture();
        LargeMapCanvas.Cursor = Cursors.Cross;
        _dragStart = null;
        if (!_dragMoved)
        {
            var point = e.GetPosition(LargeMapCanvas);
            _destination = new MapPoint(
                Math.Clamp(point.X / MapWidth, 0d, 1d),
                Math.Clamp(point.Y / MapHeight, 0d, 1d));
            RenderRouteAndMarkers();
            DestinationChanged?.Invoke(_destination);
        }

        e.Handled = true;
    }

    private void MapCanvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        ClearDestination();
        e.Handled = true;
    }

    private void ClearDestination()
    {
        _destination = null;
        RenderRouteAndMarkers();
        DestinationChanged?.Invoke(null);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = PasteDestinationFromClipboard();
            return;
        }

        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        CenterOnOwnerScreen();
        ApplyRoundedWindowRegion();
        PlayOpenAnimation();
    }

    private void PlayOpenAnimation()
    {
        OpenScaleTransform.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985d, 1d, TimeSpan.FromMilliseconds(90))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        OpenScaleTransform.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985d, 1d, TimeSpan.FromMilliseconds(90))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (IsLoaded)
        {
            ApplyRoundedWindowRegion();
        }
    }

    private void MapViewport_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var target = Math.Clamp(
            _zoom + (e.Delta > 0 ? ZoomStep : -ZoomStep),
            MinimumZoom,
            MaximumZoom);
        if (Math.Abs(target - _zoom) < 0.001d)
        {
            return;
        }

        _zoom = target;
        AnimateValue(ZoomScaleTransform, ScaleTransform.ScaleXProperty, target);
        AnimateValue(ZoomScaleTransform, ScaleTransform.ScaleYProperty, target);
        SetPan(ZoomPanTransform.X, ZoomPanTransform.Y, animate: true);
        e.Handled = true;
    }

    private void SetPan(double x, double y, bool animate)
    {
        var maxX = Math.Max(0d, MapViewbox.ActualWidth * (_zoom - 1d) / 2d);
        var maxY = Math.Max(0d, MapViewbox.ActualHeight * (_zoom - 1d) / 2d);
        var targetX = Math.Clamp(x, -maxX, maxX);
        var targetY = Math.Clamp(y, -maxY, maxY);
        if (animate)
        {
            AnimateValue(ZoomPanTransform, TranslateTransform.XProperty, targetX);
            AnimateValue(ZoomPanTransform, TranslateTransform.YProperty, targetY);
        }
        else
        {
            ZoomPanTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ZoomPanTransform.BeginAnimation(TranslateTransform.YProperty, null);
            ZoomPanTransform.X = targetX;
            ZoomPanTransform.Y = targetY;
        }
    }

    private static void AnimateValue(Animatable target, DependencyProperty property, double value)
    {
        var current = (double)target.GetValue(property);
        target.BeginAnimation(property, null);
        target.SetValue(property, value);
        target.BeginAnimation(property, new DoubleAnimation(current, value, ZoomAnimationDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private static void AnimateValue(
        UIElement target,
        DependencyProperty property,
        double from,
        double to,
        double durationMilliseconds)
    {
        target.SetValue(property, to);
        target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMilliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    private void CenterOnOwnerScreen()
    {
        var referenceHandle = Owner is null
            ? new WindowInteropHelper(this).EnsureHandle()
            : new WindowInteropHelper(Owner).Handle;
        var monitor = MonitorFromWindow(referenceHandle, MonitorDefaultToNearest);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            var workArea = SystemParameters.WorkArea;
            var side = Math.Max(360d, Math.Min(900d, Math.Min(workArea.Width, workArea.Height) - 32d));
            Width = side;
            Height = side;
            Left = workArea.Left + (workArea.Width - Width) / 2d;
            Top = workArea.Top + (workArea.Height - Height) / 2d;
            return;
        }

        var dpi = Math.Max(96u, GetDpiForWindow(referenceHandle));
        var scale = dpi / 96d;
        var workWidth = (info.Work.Right - info.Work.Left) / scale;
        var workHeight = (info.Work.Bottom - info.Work.Top) / scale;
        var fittedSide = Math.Max(360d, Math.Min(900d, Math.Min(workWidth, workHeight) - 32d));
        Width = fittedSide;
        Height = fittedSide;
        Left = info.Work.Left / scale + (workWidth - Width) / 2d;
        Top = info.Work.Top / scale + (workHeight - Height) / 2d;
    }

    private void ApplyRoundedWindowRegion()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var dpiScale = Math.Max(1d, GetDpiForWindow(handle) / 96d);
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpiScale));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpiScale));
        var radius = Math.Max(16, (int)Math.Round(28d * dpiScale));
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius, radius);
        if (region != IntPtr.Zero && SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
        }
    }

    private static Point ToCanvasPoint(MapPoint point) =>
        new(point.Left * MapWidth, point.Top * MapHeight);

    private static Brush ZoneBrush(SbtcZoneFeature feature, byte alpha)
    {
        if (!string.IsNullOrWhiteSpace(feature.Color))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(feature.Color);
                color.A = alpha;
                return new SolidColorBrush(color);
            }
            catch (FormatException)
            {
            }
        }

        var fallback = feature.Kind switch
        {
            SbtcZoneKind.Sanctuary => Color.FromArgb(alpha, 66, 221, 177),
            SbtcZoneKind.Migration => Color.FromArgb(alpha, 255, 169, 31),
            SbtcZoneKind.Patrol => Color.FromArgb(alpha, 169, 123, 243),
            _ => Color.FromArgb(alpha, 184, 168, 216)
        };
        return new SolidColorBrush(fallback);
    }

    private static Brush ZoneBrush(SbtcZoneKind kind, byte alpha) => new SolidColorBrush(kind switch
    {
        SbtcZoneKind.Sanctuary => Color.FromArgb(alpha, 66, 221, 177),
        SbtcZoneKind.Migration => Color.FromArgb(alpha, 255, 169, 31),
        SbtcZoneKind.Patrol => Color.FromArgb(alpha, 169, 123, 243),
        SbtcZoneKind.Location => Color.FromArgb(alpha, 230, 236, 242),
        _ => Color.FromArgb(alpha, 184, 168, 216)
    });

    private static SolidColorBrush BrushFrom(string color) =>
        new((Color)ColorConverter.ConvertFromString(color));

    private const uint MonitorDefaultToNearest = 2u;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr value);
}
