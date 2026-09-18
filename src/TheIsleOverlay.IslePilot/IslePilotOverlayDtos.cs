using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace TheIsleOverlay.IslePilot;

public static class IslePilotOverlayJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };
}

public sealed record IslePilotOverlayFrame
{
    [JsonPropertyName("t")]
    public string? Type { get; init; }

    [JsonPropertyName("d")]
    public IslePilotOverlayLiveDataDto? Data { get; init; }
}

public sealed record IslePilotOverlayLiveDataDto
{
    public bool? HasDino { get; init; }
    public string? SteamId { get; init; }
    public double? Growth { get; init; }
    public double? Health { get; init; }
    public double? MaxHealth { get; init; }
    public double? Hunger { get; init; }
    public double? MaxHunger { get; init; }
    public double? Thirst { get; init; }
    public double? MaxThirst { get; init; }
    public double? Stamina { get; init; }
    public double? MaxStamina { get; init; }
    public IslePilotNutritionDto? Nutrition { get; init; }
    public IslePilotOverlayPositionDto? Position { get; init; }
    public IslePilotPrimeDto? Prime { get; init; }
}

public sealed record IslePilotNutritionDto
{
    public double? Carb { get; init; }
    public double? Protein { get; init; }
    public double? Lipid { get; init; }
}

public sealed record IslePilotOverlayPositionDto
{
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Z { get; init; }
    public double? Yaw { get; init; }
}

public sealed record IslePilotOverlayMeDto
{
    public bool? HasData { get; init; }
    public bool? Online { get; init; }
    public string? SteamId { get; init; }
    public string? PersonaName { get; init; }
    public string? Name { get; init; }
    public string? Species { get; init; }
    public string? Server { get; init; }
    public string? ServerId { get; init; }
    public bool? Female { get; init; }
    public double? Growth { get; init; }
    public double? Health { get; init; }
    public double? MaxHealth { get; init; }
    public double? Hunger { get; init; }
    public double? MaxHunger { get; init; }
    public double? Thirst { get; init; }
    public double? MaxThirst { get; init; }
    public double? Stamina { get; init; }
    public double? MaxStamina { get; init; }
    public IslePilotNutritionDto? Nutrition { get; init; }
    public IslePilotPrimeDto? Prime { get; init; }
}

public sealed record IslePilotPrimeDto
{
    public bool? Elder { get; init; }
    public bool? Eligible { get; init; }
    public int? Done { get; init; }
    public int? Required { get; init; }
    public IReadOnlyList<IslePilotPrimeQuestDto> Quests { get; init; } = [];
}

public sealed record IslePilotPrimeQuestDto
{
    public string? Name { get; init; }
    public bool? Done { get; init; }
}

public sealed record IslePilotOverlayMapDto
{
    public bool? LiveMapEnabled { get; init; }
    public bool? Allowed { get; init; }
    public string? Reason { get; init; }
    public IslePilotMapCalibrationDto? Calibration { get; init; }
    public IReadOnlyList<IslePilotOverlayMapMarkerDto> Markers { get; init; } = [];
    public IReadOnlyList<IslePilotOverlayMapCategoryDto> Categories { get; init; } = [];
    public IReadOnlyList<IslePilotOverlayMapPoiDto> Pois { get; init; } = [];
}

public sealed record IslePilotOverlayMarkersDto
{
    public bool Ok { get; init; }
    public IReadOnlyList<IslePilotOverlayMapMarkerDto> Markers { get; init; } = [];
}

public sealed record IslePilotOverlayGarageDto
{
    public IslePilotOverlayGarageSettingsDto? Settings { get; init; }
    public IReadOnlyList<IslePilotOverlayGarageDinoDto> Dinos { get; init; } = [];
}

public sealed record IslePilotOverlayGarageSettingsDto
{
    public bool? SellingEnabled { get; init; }
    public string? CurrencyName { get; init; }
    public bool? LiveSwap { get; init; }
    public bool? SelfSlayEnabled { get; init; }
    public bool? MutationPickEnabled { get; init; }
}

public sealed record IslePilotOverlayGarageDinoDto
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Species { get; init; }
    public IslePilotOverlaySkinDraftPayloadDto? Payload { get; init; }
    public string? ClassName { get; init; }
    public string? Gender { get; init; }
    public double? Growth { get; init; }
    public double? Health { get; init; }
    public double? Hunger { get; init; }
    public double? Thirst { get; init; }
    public double? Stamina { get; init; }
    public bool? IsPrimeElder { get; init; }
    public DateTimeOffset? ParkedAt { get; init; }
    public IslePilotOverlayGaragePaletteDto? Palette { get; init; }
    public double? SellPrice { get; init; }
    public bool? MutationEligible { get; init; }
    public IReadOnlyList<string> PickableMutations { get; init; } = [];
}

public sealed record IslePilotOverlayGaragePaletteDto
{
    public string? Body { get; init; }
    public string? Markings { get; init; }
    public string? Flank { get; init; }
    public string? Underbelly { get; init; }
    public string? Detail { get; init; }
    public string? Display { get; init; }
    public string? Eyes { get; init; }
    public string? Teeth { get; init; }
    public string? Mouth { get; init; }
    public string? Claws { get; init; }
}

public sealed record IslePilotOverlaySkinDraftsDto
{
    public IReadOnlyList<IslePilotOverlaySkinDraftDto> Drafts { get; init; } = [];
}

public sealed record IslePilotOverlaySkinDraftDto
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? Species { get; init; }
    public string? Sex { get; init; }
    public int? Variation { get; init; }
    public int? Pattern { get; init; }
    public int? Theme { get; init; }
    public string? RenderMode { get; init; }
    public IslePilotOverlaySkinDraftPayloadDto? Payload { get; init; }
    public IslePilotOverlayGaragePaletteDto? Palette { get; init; }
    public IslePilotOverlayGaragePaletteDto? Colors { get; init; }
    public IslePilotOverlayGaragePaletteDto? Color { get; init; }
    public IslePilotOverlayGaragePaletteDto? Skin { get; init; }
    // Older skin-draft responses expose the palette fields directly on the draft.
    public string? Body { get; init; }
    public string? Markings { get; init; }
    public string? Flank { get; init; }
    public string? Underbelly { get; init; }
    public string? Detail { get; init; }
    public string? Display { get; init; }
    public string? Eyes { get; init; }
    public string? Teeth { get; init; }
    public string? Mouth { get; init; }
    public string? Claws { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; init; }

    public string? GetSpecies() =>
        Species
        ?? Payload?.Species
        ?? Payload?.Class
        ?? TryGetString(ExtraFields, "species")
        ?? TryGetString(ExtraFields, "class")
        ?? TryGetString(Payload?.ExtraFields, "species")
        ?? TryGetString(Payload?.ExtraFields, "class");

    private static string? TryGetString(Dictionary<string, JsonElement>? dict, string key) =>
        dict is not null && dict.TryGetValue(key, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString()
            : null;

    public IslePilotOverlaySkinDraftPayloadDto? GetPayload()
    {
        if (Payload is not null)
        {
            var pPalette = Payload.Palette ?? GetPalette();
            var pSpecies = Payload.Species ?? Payload.Class ?? GetSpecies();
            return Payload with
            {
                Species = pSpecies,
                Palette = pPalette
            };
        }
        var palette = GetPalette();
        if (palette is null && Sex is null && Variation is null && Pattern is null && Theme is null && RenderMode is null)
            return null;
        return new IslePilotOverlaySkinDraftPayloadDto
        {
            Species = GetSpecies(),
            Sex = Sex,
            Variation = Variation ?? 0,
            Pattern = Pattern ?? 0,
            Theme = Theme ?? 0,
            RenderMode = RenderMode ?? "standard",
            Palette = palette
        };
    }

    public static double LinearToSrgb(double c) =>
        c <= 0.0031308
            ? c * 12.92
            : 1.055 * Math.Pow(Math.Max(0, c), 1.0 / 2.4) - 0.055;

    public static string LinearRgbaToHex(double r, double g, double b)
    {
        var red = (int)Math.Clamp(Math.Round(LinearToSrgb(r) * 255.0), 0, 255);
        var green = (int)Math.Clamp(Math.Round(LinearToSrgb(g) * 255.0), 0, 255);
        var blue = (int)Math.Clamp(Math.Round(LinearToSrgb(b) * 255.0), 0, 255);
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    public static string? TryExtractHexColor(object? source)
    {
        if (source is null) return null;
        if (source is string str) return NormalizeHex(str);
        if (source is JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return NormalizeHex(element.GetString());
            if (element.ValueKind == JsonValueKind.Array)
            {
                var values = new List<double>(4);
                foreach (var item in element.EnumerateArray())
                {
                    if (item.TryGetDouble(out var d))
                        values.Add(d);
                }
                if (values.Count >= 3)
                    return LinearRgbaToHex(values[0], values[1], values[2]);
            }
        }
        return null;
    }

    public IslePilotOverlayGaragePaletteDto? GetPalette()
    {
        string? FindColor(params string[] candidateKeys)
        {
            foreach (var key in candidateKeys)
            {
                if (Payload?.ExtraFields is not null && Payload.ExtraFields.TryGetValue(key, out var pe))
                {
                    var hex = TryExtractHexColor(pe);
                    if (hex is not null) return hex;
                }
                if (ExtraFields is not null && ExtraFields.TryGetValue(key, out var ee))
                {
                    var hex = TryExtractHexColor(ee);
                    if (hex is not null) return hex;
                }
            }
            return null;
        }

        var basePalette = Payload?.Palette ?? Palette ?? Colors ?? Color ?? Skin;
        var body = NormalizeHex(basePalette?.Body) ?? NormalizeHex(Body) ?? FindColor("body");
        var markings = NormalizeHex(basePalette?.Markings) ?? NormalizeHex(Markings) ?? FindColor("markings");
        var flank = NormalizeHex(basePalette?.Flank) ?? NormalizeHex(Flank) ?? FindColor("flank");
        var underbelly = NormalizeHex(basePalette?.Underbelly) ?? NormalizeHex(Underbelly) ?? FindColor("underbelly");
        var detail = NormalizeHex(basePalette?.Detail) ?? NormalizeHex(Detail) ?? FindColor("detail1", "detail");
        var display = NormalizeHex(basePalette?.Display) ?? NormalizeHex(Display) ?? FindColor("male_display", "display");
        var eyes = NormalizeHex(basePalette?.Eyes) ?? NormalizeHex(Eyes) ?? FindColor("eyes");
        var teeth = NormalizeHex(basePalette?.Teeth) ?? NormalizeHex(Teeth) ?? FindColor("teeth");
        var mouth = NormalizeHex(basePalette?.Mouth) ?? NormalizeHex(Mouth) ?? FindColor("mouth");
        var claws = NormalizeHex(basePalette?.Claws) ?? NormalizeHex(Claws) ?? FindColor("claws");

        if (body is null && markings is null && flank is null && underbelly is null &&
            detail is null && display is null && eyes is null && teeth is null &&
            mouth is null && claws is null)
        {
            return null;
        }

        return new IslePilotOverlayGaragePaletteDto
        {
            Body = body,
            Markings = markings,
            Flank = flank,
            Underbelly = underbelly,
            Detail = detail,
            Display = display,
            Eyes = eyes,
            Teeth = teeth,
            Mouth = mouth,
            Claws = claws
        };
    }

    public static string? NormalizeHex(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        var raw = trimmed.StartsWith("#", StringComparison.Ordinal) ? trimmed[1..] : trimmed;
        return System.Text.RegularExpressions.Regex.IsMatch(raw, "^[0-9a-fA-F]{6}$")
            ? $"#{raw.ToUpperInvariant()}"
            : (trimmed.StartsWith("#", StringComparison.Ordinal) && trimmed.Length == 7 ? trimmed.ToUpperInvariant() : null);
    }
}

public sealed record IslePilotOverlaySkinDraftPayloadDto
{
    public string? Id { get; init; }
    public string? Species { get; init; }
    [JsonPropertyName("class")]
    public string? Class { get; init; }
    public string? Sex { get; init; }
    [JsonPropertyName("female")]
    public bool? Female { get; init; }
    public string? Name { get; init; }
    public int Variation { get; init; }
    public int Pattern { get; init; }
    public int Theme { get; init; }
    public string RenderMode { get; init; } = "standard";
    public IslePilotOverlayGaragePaletteDto? Palette { get; init; }
    public DateTimeOffset? CreatedAt { get; init; }
    public IslePilotOverlaySkinGlitchLabDto? GlitchLab { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtraFields { get; init; }
}

public sealed record IslePilotOverlaySkinGlitchLabDto
{
    public int Pi { get; init; }
    public int Sv { get; init; }
    public IReadOnlyDictionary<string, IslePilotOverlaySkinGlitchLayerDto> Layers { get; init; } =
        new Dictionary<string, IslePilotOverlaySkinGlitchLayerDto>();
}

public sealed record IslePilotOverlaySkinGlitchLayerDto
{
    public int? A { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }
}

internal sealed record IslePilotOverlaySkinDraftRequest(
    string Slug,
    IReadOnlyList<IslePilotOverlaySkinDraftSaveItem> Drafts);

internal sealed record IslePilotOverlaySkinDraftSaveItem(
    string Name,
    string Species,
    IslePilotOverlaySkinDraftPayloadDto Payload);

internal sealed record IslePilotOverlaySkinApplyRequest(
    string ServerId,
    IslePilotOverlaySkinSetPayloadDto Payload);

internal sealed record IslePilotOverlaySkinSetPayloadDto
{
    private static readonly Dictionary<string, string> CanonicalBlueprintClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Allosaurus"] = "BP_Allosaurus_C",
        ["Austroraptor"] = "BP_Austroraptor_C",
        ["Beipiaosaurus"] = "BP_Beipiaosaurus_C",
        ["Carnotaurus"] = "BP_Carnotaurus_C",
        ["Ceratosaurus"] = "BP_Ceratosaurus_C",
        ["Deinosuchus"] = "BP_Deinosuchus_C",
        ["Diabloceratops"] = "BP_Diabloceratops_C",
        ["Dilophosaurus"] = "BP_Dilophosaurus_C",
        ["Dryosaurus"] = "BP_Dryosaurus_C",
        ["Gallimimus"] = "BP_Gallimimus_C",
        ["Herrerasaurus"] = "BP_Herrerasaurus_C",
        ["Hypsilophodon"] = "BP_Hypsilophodon_C",
        ["Kentrosaurus"] = "BP_Kentrosaurus_C",
        ["Maiasaura"] = "BP_Maiasaura_C",
        ["Omniraptor"] = "BP_Omniraptor_C",
        ["Pachycephalosaurus"] = "BP_Pachycephalosaurus_C",
        ["Pteranodon"] = "BP_Pteranodon_C",
        ["Stegosaurus"] = "BP_Stegosaurus_C",
        ["Tenontosaurus"] = "BP_Tenontosaurus_C",
        ["Triceratops"] = "BP_Triceratops_C",
        ["Troodon"] = "BP_Troodon_C",
        ["Tyrannosaurus"] = "BP_Tyrannosaurus_C",
    };

    public static string CanonicalBlueprintClass(string? species)
    {
        if (string.IsNullOrWhiteSpace(species)) return "BP_Tyrannosaurus_C";
        var normalized = species.Trim().Replace(' ', '_');
        if (normalized.EndsWith("_C", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2];
        }
        if (normalized.StartsWith("BP_", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[3..];
        }
        if (CanonicalBlueprintClasses.TryGetValue(normalized, out var canonical))
        {
            return canonical;
        }

        var pascal = string.Concat(normalized.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => char.ToUpperInvariant(part[0]) + (part.Length > 1 ? part[1..].ToLowerInvariant() : string.Empty)));
        return $"BP_{pascal}_C";
    }

    public double[] Body { get; init; } = [];
    [JsonPropertyName("class")] public string Class { get; init; } = string.Empty;
    public double[] Claws { get; init; } = [];
    [JsonPropertyName("detail1")] public double[] Detail1 { get; init; } = [];
    public double[] Eyes { get; init; } = [];
    public bool Female { get; init; }
    public double[] Flank { get; init; } = [];
    [JsonPropertyName("male_display")] public double[] MaleDisplay { get; init; } = [];
    public double[] Markings { get; init; } = [];
    public double[] Mouth { get; init; } = [];
    public int Pattern { get; init; }
    public double[] Teeth { get; init; } = [];
    public int Theme { get; init; }
    public double[] Underbelly { get; init; } = [];
    public int Variation { get; init; }

    public static IslePilotOverlaySkinSetPayloadDto FromPalette(
        string species,
        IslePilotOverlayGaragePaletteDto palette,
        bool female = true,
        int theme = 0,
        int pattern = 0,
        int variation = 0)
    {
        static double SrgbToLinear(double c) =>
            c <= 0.04045
                ? c / 12.92
                : Math.Pow((c + 0.055) / 1.055, 2.4);

        static double[] Rgba(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return [0, 0, 0, 1];
            var hex = value.Trim().TrimStart('#');
            if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
                throw new ArgumentException("Bảng màu phải dùng mã hex dạng #RRGGBB.", nameof(palette));
            var r = (rgb >> 16 & 255) / 255d;
            var g = (rgb >> 8 & 255) / 255d;
            var b = (rgb & 255) / 255d;
            return [
                Math.Round(SrgbToLinear(r), 6),
                Math.Round(SrgbToLinear(g), 6),
                Math.Round(SrgbToLinear(b), 6),
                1
            ];
        }

        var blueprintClass = CanonicalBlueprintClass(species);
        var displayRgba = Rgba(palette.Display);

        return new IslePilotOverlaySkinSetPayloadDto
        {
            Body = Rgba(palette.Body),
            Class = blueprintClass,
            Claws = Rgba(palette.Claws),
            Detail1 = Rgba(palette.Detail),
            Eyes = Rgba(palette.Eyes),
            Female = female,
            Flank = Rgba(palette.Flank),
            MaleDisplay = displayRgba,
            Markings = Rgba(palette.Markings),
            Mouth = Rgba(palette.Mouth),
            Pattern = pattern,
            Teeth = Rgba(palette.Teeth),
            Theme = theme,
            Underbelly = Rgba(palette.Underbelly),
            Variation = variation
        };
    }

    public static IslePilotOverlaySkinSetPayloadDto FromDraft(
        string species, IslePilotOverlaySkinDraftPayloadDto payload, bool? female = null)
    {
        var isFemale = female ?? (!string.Equals(payload.Sex, "male", StringComparison.OrdinalIgnoreCase));
        return FromPalette(
            species,
            payload.Palette ?? new IslePilotOverlayGaragePaletteDto(),
            isFemale,
            payload.Theme,
            payload.Pattern,
            payload.Variation);
    }
}

public sealed record IslePilotOverlaySkinApplyDto
{
    public bool Ok { get; init; }
    public bool? Success { get; init; }
    public bool? Pending { get; init; }
    public string? CommandId { get; init; }
    public string? Error { get; init; }
    public string? Message { get; init; }

    public bool Accepted => Ok || Success == true;
}

public sealed record IslePilotOverlayGarageCommandDto
{
    public bool Ok { get; init; }
    public bool? Pending { get; init; }
    public int? DelaySec { get; init; }
    public string? CommandId { get; init; }
    public string? Error { get; init; }
}

public sealed record IslePilotOverlayGarageCommandStatusDto
{
    public string? Status { get; init; }
    public string? Error { get; init; }
}

internal sealed record IslePilotOverlayGarageParkRequest(string Step);

public sealed record IslePilotMapCalibrationDto
{
    public IslePilotMapCalibrationPointDto? A { get; init; }
    public IslePilotMapCalibrationPointDto? B { get; init; }
}

public sealed record IslePilotMapCalibrationPointDto
{
    public double WorldX { get; init; }
    public double WorldY { get; init; }
    public double U { get; init; }
    public double V { get; init; }
}

public sealed record IslePilotOverlayMapMarkerDto
{
    public string? SteamId { get; init; }
    public string? Label { get; init; }
    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Z { get; init; }
    public double? Yaw { get; init; }
    public bool Self { get; init; }
    public bool Group { get; init; }
    public IReadOnlyList<IslePilotOverlayWorldPointDto> Path { get; init; } = [];
}

public sealed record IslePilotOverlayWorldPointDto
{
    public double? X { get; init; }
    public double? Y { get; init; }
}

public sealed record IslePilotOverlayMapCategoryDto
{
    public string? Id { get; init; }
    public string? Name { get; init; }
}

public sealed record IslePilotOverlayMapPoiDto
{
    public string? Id { get; init; }
    public string? Name { get; init; }
    public string? CategoryId { get; init; }
    public string? Shape { get; init; }
    public double? Size { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public bool? Enabled { get; init; }
    public bool? HideLabel { get; init; }
    public IReadOnlyList<IslePilotOverlayWorldPointDto> Points { get; init; } = [];
}
