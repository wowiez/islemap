using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TheIsleOverlay.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        // Give every window the embedded logo icon. Without it the taskbar button
        // falls back to the exe icon, which Windows caches per path and kept
        // showing the old compass logo after updates.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window { Icon: null } window && WindowIcon.Value is { } icon) window.Icon = icon;
            }));
    }

    private static readonly Lazy<BitmapFrame?> WindowIcon = new(() =>
    {
        try
        {
            return BitmapFrame.Create(new Uri("pack://application:,,,/IsleLiveMap;component/Assets/IsleLiveMap.ico", UriKind.Absolute));
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or FileFormatException)
        {
            return null; // keep the exe icon rather than fail window creation
        }
    });

    public static App CurrentApp => (App)Current;

    /// <summary>The brand font (Lexend) for text built in code.</summary>
    public static System.Windows.Media.FontFamily UiFont =>
        Current?.TryFindResource("WieFont") as System.Windows.Media.FontFamily ??
        new System.Windows.Media.FontFamily("Segoe UI");

    private static void App_DispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReporter.Write("WPF dispatcher", e.Exception);
        e.Handled = true;
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CrashReporter.Write("AppDomain", exception);
        }
    }

    private static void TaskScheduler_UnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        CrashReporter.Write("Unobserved task", e.Exception);
        e.SetObserved();
    }
}
