using System.Text.Json;
using System.Text.Json.Serialization;

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
