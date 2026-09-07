using System.IO;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App;

internal static class LegacyAppDataMigration
{
    private static readonly string[] LegacyVendorDirectories =
    [
        string.Join(string.Empty, "Wo", "wiez"),
        string.Join(string.Empty, "K", "Long", "Dev")
    ];

    public static void Run()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installerOwnedRoot = Path.Combine(localAppData, "IsleLiveMap");
        var legacyRoots = LegacyVendorDirectories.Select(
            vendor => Path.Combine(localAppData, vendor, "IsleLiveMap"));

        // Versions 1.7.10-1.7.12 accidentally wrote user data beside the
        // Velopack installation. Read from there first, but never delete it.
        Run([installerOwnedRoot], AppPaths.Root, deleteSources: false);
        Run(legacyRoots, AppPaths.Root, deleteSources: true);
    }

    internal static void Run(
        IEnumerable<string> legacyRoots,
        string currentRoot,
        bool deleteSources = true)
    {
        var removableRoots = new List<string>();
        try
        {
            currentRoot = Path.GetFullPath(currentRoot);
            foreach (var candidate in legacyRoots)
            {
                var legacyRoot = Path.GetFullPath(candidate);
                if (!Directory.Exists(legacyRoot) || PathsEqual(legacyRoot, currentRoot))
                {
                    continue;
                }

                CopyFileIfMissing(legacyRoot, currentRoot, "overlay-layout.json");
                CopyDirectoryIfMissing(legacyRoot, currentRoot, "WebView2");
                CopyDirectoryIfMissing(legacyRoot, currentRoot, "WebView2-IslePilot");

                var legacyCredential = Path.Combine(legacyRoot, "islepilot-overlay.credential");
                var currentCredential = Path.Combine(currentRoot, "islepilot-overlay.credential");
                var credentialsSafe = new IslePilotCredentialStore(currentCredential)
                    .MigrateLegacyAsync(legacyCredential)
                    .GetAwaiter()
                    .GetResult();
                if (credentialsSafe && deleteSources)
                {
                    removableRoots.Add(legacyRoot);
                }
            }

            foreach (var removableRoot in removableRoots)
            {
                Directory.Delete(removableRoot, recursive: true);
                DeleteParentIfEmpty(removableRoot);
            }
        }
        catch (Exception exception)
        {
            // Updating must never make the application unusable. A source directory
            // remains untouched whenever its credential could not be recovered.
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

    private static void DeleteParentIfEmpty(string removedRoot)
    {
        var parent = Directory.GetParent(removedRoot)?.FullName;
        if (parent is not null &&
            Directory.Exists(parent) &&
            !Directory.EnumerateFileSystemEntries(parent).Any())
        {
            Directory.Delete(parent, recursive: false);
        }
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
}
