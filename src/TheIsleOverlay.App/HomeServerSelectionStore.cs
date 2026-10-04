using System.IO;
using System.Text.Json;

namespace TheIsleOverlay.App;

// Store only the server identifier; account secrets stay in their existing vault.
internal sealed class HomeServerSelectionStore
{
    internal const string IslePilot = "islepilot";
    private readonly string _path;

    public HomeServerSelectionStore(string? path = null) => _path = Path.GetFullPath(path ??
        Environment.GetEnvironmentVariable("ISLELIVEMAP_HOME_SELECTION_PATH") ??
        Path.Combine(AppPaths.Root, "home-server.json"));

    public string? Load()
    {
        try { return Normalize(JsonSerializer.Deserialize<Selection>(File.ReadAllText(_path))?.SourceId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    public void Save(string? sourceId)
    {
        if (Normalize(sourceId) is not { } valid) return;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Selection(valid)));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static string? Normalize(string? id) =>
        string.Equals(id, IslePilot, StringComparison.OrdinalIgnoreCase) ? IslePilot :
        TelemetrySourceDefinition.FromId(id)?.Id;

    private sealed record Selection(string SourceId);
}
