using System.IO;
using System.Reflection;
using System.Text;

namespace TheIsleOverlay.App;

internal static class CrashReporter
{
    private static readonly object FileLock = new();

    public static void Write(string origin, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Root);
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            var entry = new StringBuilder()
                .AppendLine("------------------------------------------------------------")
                .AppendLine($"Time: {DateTimeOffset.Now:O}")
                .AppendLine($"Origin: {origin}")
                .AppendLine($"Version: {version}")
                .AppendLine($"OS: {Environment.OSVersion}")
                .AppendLine(exception.ToString())
                .ToString();

            lock (FileLock)
            {
                File.AppendAllText(AppPaths.CrashLog, entry);
            }
        }
        catch
        {
            // Crash reporting must never cause a second crash.
        }
    }
}
