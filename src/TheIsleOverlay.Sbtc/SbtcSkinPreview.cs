using System.Text.Json.Nodes;

namespace TheIsleOverlay.Sbtc;

public sealed record SbtcSkinPreview(string Build, string? PatternAsset, string? UtilityAsset,
    IReadOnlyDictionary<string, string> UtilityChannels)
{
    public static SbtcSkinPreview Basic { get; } = new("", null, null, new Dictionary<string, string>());
}

public sealed partial class SbtcIslandVaultClient
{
    public async Task<SbtcSkinPreview> GetSkinPreviewAsync(string species, int pattern,
        CancellationToken cancellationToken = default)
    {
        var manifest = await SendAsync<JsonObject>(HttpMethod.Get, "api/studio/skin-contract", null, cancellationToken);
        manifest = manifest?["manifest"] as JsonObject ?? manifest;
        if (Number(manifest, "schema_version") != 2 || Text(manifest, "build") is not { Length: > 0 } build)
            return SbtcSkinPreview.Basic;
        var entry = (manifest?["species"] as JsonObject)?.FirstOrDefault(p =>
            SpeciesKey(p.Key) == SpeciesKey(species));
        if (entry?.Value is not JsonObject definition) return SbtcSkinPreview.Basic;
        var canonical = entry.Value.Key;
        var row = (definition["patterns"] as JsonArray)?.OfType<JsonObject>()
            .FirstOrDefault(p => Number(p, "index") == pattern);
        var material = definition["material"] as JsonObject;
        var patternAsset = PreviewAssetName(canonical, Text(row, "preview_asset"));
        var utilityAsset = PreviewAssetName(canonical, Text(material, "preview_asset"));
        var channels = new Dictionary<string, string>();
        var seen = new HashSet<string>();
        if (utilityAsset is not null && material?["preview_channel_mapping"] is JsonObject mapping)
        {
            foreach (var slot in new[] { "teeth", "mouth", "claws" })
            {
                if (Text(mapping, slot) is not { } channel) continue;
                if (channel is not ("r" or "g" or "b") || !seen.Add(channel))
                {
                    channels.Clear();
                    break;
                }
                // Match the studio's measured-channel corrections for this asset build.
                var measured = build == "24664709" ? (canonical, slot) switch
                {
                    ("Carnotaurus", "teeth") => "r",
                    ("Herrerasaurus" or "Omniraptor" or "Troodon", "mouth") => "g",
                    ("Dryosaurus" or "Herrerasaurus" or "Omniraptor" or "Pachycephalosaurus" or "Triceratops", "claws") => "b",
                    _ => null
                } : null;
                if (measured is null || measured == channel) channels.Add(slot, channel);
            }
        }
        return new(build, patternAsset, channels.Count > 0 ? utilityAsset : null, channels);
    }

    private static string? PreviewAssetName(string species, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim().Replace('\\', '/');
        if (path.StartsWith("/assets/dino/", StringComparison.Ordinal)) path = path[13..];
        else if (path.StartsWith('/')) return null;
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && (parts[0] == species || parts[0] == "assets_current")) path = parts[1];
        else if (parts.Length != 1 || path.StartsWith('/')) return null;
        return path.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-') &&
            !path.Contains("..", StringComparison.Ordinal) &&
            (path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                ? path : null;
    }
}
