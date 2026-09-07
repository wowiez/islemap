using System.IO;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

internal static class LegacyAppDataMigration
{
    private static readonly string LegacyVendorDirectory =
        string.Join(string.Empty, "K", "Long", "Dev");

    public static void Run()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var legacyRoot = Path.Combine(localAppData, LegacyVendorDirectory, "IsleLiveMap");
        Run(legacyRoot, AppPaths.Root);
    }

    internal static void Run(string legacyRoot, string currentRoot)
    {
        try
        {
            legacyRoot = Path.GetFullPath(legacyRoot);
            currentRoot = Path.GetFullPath(currentRoot);
            if (!Directory.Exists(legacyRoot) || PathsEqual(legacyRoot, currentRoot))
            {
                return;
            }

            CopyFileIfMissing(legacyRoot, currentRoot, "overlay-layout.json");
            CopyDirectoryIfMissing(legacyRoot, currentRoot, "WebView2");
            CopyDirectoryIfMissing(legacyRoot, currentRoot, "WebView2-IslePilot");

            var legacyCredential = Path.Combine(legacyRoot, "islepilot-overlay.credential");
            var currentCredential = Path.Combine(currentRoot, "islepilot-overlay.credential");
            new IslePilotCredentialStore(currentCredential)
                .MigrateLegacyAsync(legacyCredential)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            // Updating must never make the application unusable. The regular login
            // flow remains available if old data is locked, corrupt, or inaccessible.
            CrashReporter.Write("Legacy app-data migration", exception);
        }
    }

    private static void CopyFileIfMissing(string sourceRoot, string targetRoot, string name)
    {
        var source = Path.Combine(sourceRoot, name);
        var target = Path.Combine(targetRoot, name);
        if (!File.Exists(source) || File.Exists(target))
        {
            return;
        }

        Directory.CreateDirectory(targetRoot);
        File.Copy(source, target, overwrite: false);
    }

    private static void CopyDirectoryIfMissing(string sourceRoot, string targetRoot, string name)
    {
        var source = Path.Combine(sourceRoot, name);
        var target = Path.Combine(targetRoot, name);
        if (!Directory.Exists(source) || Directory.Exists(target))
        {
            return;
        }

        CopyDirectory(source, target);
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
