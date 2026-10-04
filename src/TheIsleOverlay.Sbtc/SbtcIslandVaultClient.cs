using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TheIsleOverlay.Core;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Sbtc;

/// <summary>
/// The SBTC Island vault and skin studio, exposed with the same shapes the overlay's
/// garage and skin editor already speak (<see cref="IslePilotOverlayGarageDto"/> and
/// friends), so the F8 pages work against this site exactly as they did against an
/// IslePilot server.
///
/// Endpoints used (all need the Steam session except the palettes):
/// <list type="bullet">
/// <item>GET  /api/vault           → the parked dinos</item>
/// <item>POST /api/vault/park      → park the dino that is out right now</item>
/// <item>POST /api/vault/name/{id} → rename a parked dino</item>
/// <item>POST /api/vault/skin/{id} → apply a saved design to a parked dino</item>
/// <item>GET  /api/designs         → the saved designs</item>
/// <item>GET  /api/palettes        → the default palette of every species (public)</item>
/// </list>
/// </summary>
public sealed partial class SbtcIslandVaultClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new SbtcColorConverter() }
    };

    private readonly HttpClient _httpClient;
    private readonly SbtcIslandOptions _options;
    private readonly Dictionary<string, (string Kind, double IssuedAt)> _commands = new();

    public SbtcIslandVaultClient(HttpClient httpClient, SbtcIslandOptions options)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.SessionCookieHeader))
        {
            throw new ArgumentException("An SBTC Island Steam session is required.", nameof(options));
        }
    }

    public async Task<IslePilotOverlayGarageDto> GetVaultAsync(CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync<SbtcVaultResponse>(HttpMethod.Get, "api/vault", body: null, cancellationToken);
        if (payload?.Ok != true || payload.DbConfigured == false || payload.DbPresent == false || payload.StorageOn == false)
            throw new HttpRequestException(payload?.Message ?? "Kho Dino SBTC hiện không khả dụng.");
        var dinos = new List<IslePilotOverlayGarageDinoDto>();
        foreach (var slot in payload?.List ?? [])
        {
            dinos.Add(ToGarageDino(slot));
        }

        return new IslePilotOverlayGarageDto
        {
            Settings = new IslePilotOverlayGarageSettingsDto
            {
                SellingEnabled = false,
                MutationPickEnabled = false,
                LiveSwap = false
            },
            Dinos = dinos
        };
    }

    public async Task<IslePilotOverlayGarageCommandDto> ParkAsync(CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync<SbtcCommandResponse>(HttpMethod.Post, "api/vault/park", new { }, cancellationToken);
        // Some accounts use the Cave bridge instead of ordinary vault storage.
        if (payload?.Error == "cave_bridge")
        {
            return new() { Ok = false, Error = "Tài khoản dùng Cave. Kiểm tra phí lưu kho và xác nhận lại trước khi cất Dino." };
        }
        return TrackCommand(payload, "park");
    }

    public async Task<IslePilotOverlayGarageCommandDto> RestoreAsync(string dinoId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dinoId)) throw new ArgumentException("Dino ID is required.", nameof(dinoId));
        var payload = await SendAsync<SbtcCommandResponse>(HttpMethod.Post, "api/vault/redeem", new { dino_id = dinoId }, cancellationToken);
        return TrackCommand(payload, "redeem");
    }

    private IslePilotOverlayGarageCommandDto TrackCommand(SbtcCommandResponse? payload, string kind)
    {
        var command = ToCommand(payload);
        if (!command.Ok) return command;
        var id = Guid.NewGuid().ToString("N");
        _commands[id] = (kind, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d);
        return command with { Pending = true, CommandId = id };
    }

    public async Task<IslePilotOverlayGarageCommandStatusDto> GetCommandStatusAsync(string commandId, CancellationToken cancellationToken = default)
    {
        if (commandId.StartsWith("cave:", StringComparison.Ordinal))
        {
            var cave = await SendAsync<SbtcCaveStatus>(HttpMethod.Get,
                "api/cave/status?id=" + Uri.EscapeDataString(commandId[5..]), null, cancellationToken);
            return new() { Status = cave?.Status == "refused" ? "failed" : cave?.Status, Error = cave?.Detail };
        }
        if (!_commands.TryGetValue(commandId, out var command)) return new() { Status = "failed", Error = "Không tìm thấy yêu cầu." };
        var vault = await SendAsync<SbtcVaultResponse>(HttpMethod.Get, "api/vault", null, cancellationToken);
        var outcome = command.Kind == "park" ? vault?.ParkOutcome : vault?.RedeemOutcome;
        // Ignore an acknowledgement left over from an earlier request.
        if (vault?.Ok != true || outcome?.At is not { } at || at < command.IssuedAt - 15)
            return new() { Status = "pending" };
        var status = outcome.Status switch
        {
            "done" when outcome.Verified != false => "done",
            "delivered" => "done",
            "failed" or "returned" => "failed",
            _ => "pending"
        };
        if (status is "done" or "failed") _commands.Remove(commandId);
        return new() { Status = status, Error = outcome.Message };
    }

    public async Task<IslePilotOverlayGarageCommandDto> ApplyDesignAsync(
        string dinoId,
        string designId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(designId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return new IslePilotOverlayGarageCommandDto { Ok = false, Error = "Design không hợp lệ." };
        }

        var payload = await SendAsync<SbtcCommandResponse>(
            HttpMethod.Post,
            $"api/vault/skin/{Uri.EscapeDataString(dinoId)}",
            new { design_id = id },
            cancellationToken);
        return ToCommand(payload);
    }

    public async Task<string?> RenameAsync(
        string dinoId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync<SbtcRenameResponse>(
            HttpMethod.Post,
            $"api/vault/name/{Uri.EscapeDataString(dinoId)}",
            new { name },
            cancellationToken);
        return payload?.Name;
    }

    public async Task<IslePilotOverlaySkinDraftsDto> GetDesignsAsync(CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync<SbtcDesignsResponse>(HttpMethod.Get, "api/designs", body: null, cancellationToken);
        if (payload?.SignedIn == false) throw new SbtcIslandAuthenticationException("Phiên studio SBTC đã hết hạn. Đăng nhập Steam lại.");
        if (payload is null || payload.Ok == false || payload.Designs is null)
            throw new HttpRequestException("Không tải được thiết kế SBTC.");
        var drafts = new List<IslePilotOverlaySkinDraftDto>();
        foreach (var design in payload?.Designs ?? [])
        {
            SbtcSkinRecipe? recipe = null;
            if (design.Recipe is { ValueKind: JsonValueKind.Object } rawRecipe)
            {
                try { recipe = rawRecipe.Deserialize<SbtcSkinRecipe>(JsonOptions); }
                catch (JsonException) { /* Unsupported recipe: keep the saved row visible. */ }
            }
            var hasRecipe = design.Recipe is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined };
            var palette = recipe?.ToPalette() ?? (hasRecipe ? null : PaletteFrom(design.Palette ?? design.Colors ?? design.Skin));
            drafts.Add(new IslePilotOverlaySkinDraftDto
            {
                Id = design.Id?.ToString(CultureInfo.InvariantCulture),
                Name = design.Name,
                Species = design.Species,
                Sex = design.Sex,
                Variation = recipe?.Variation ?? design.Variation,
                Pattern = recipe?.Pattern ?? design.Pattern,
                Theme = recipe?.Theme ?? design.Theme,
                RenderMode = recipe?.Variation == 0 ? "glitch" : "standard",
                Palette = palette,
                Colors = palette,
                ExtraFields = recipe?.ContractVersion is { } version
                    ? new() { ["sbtc_contract_version"] = JsonSerializer.SerializeToElement(version) } : null
            });
        }

        return new IslePilotOverlaySkinDraftsDto { Drafts = drafts };
    }

    /// <summary>
    /// The default palette of every species, straight from the public endpoint. Colours
    /// arrive as linear RGBA. Studio recipes instead use picker RGB / 255.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IslePilotOverlayGaragePaletteDto>> GetPalettesAsync(
        CancellationToken cancellationToken = default)
    {
        var payload = await SendAsync<SbtcPalettesResponse>(HttpMethod.Get, "api/palettes", body: null, cancellationToken);
        var result = new Dictionary<string, IslePilotOverlayGaragePaletteDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var (species, palette) in payload?.Palettes ?? [])
        {
            var converted = PaletteFrom(palette);
            if (converted is not null)
            {
                result[species] = converted;
            }
        }

        return result;
    }

    private async Task<T?> SendAsync<T>(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
        where T : class
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        cancellationToken = timeout.Token;
        var query = string.IsNullOrWhiteSpace(_options.Platform) || path.StartsWith("api/designs", StringComparison.Ordinal) ||
            path.StartsWith("api/glitchcreator", StringComparison.Ordinal)
            ? string.Empty : $"{(path.Contains('?') ? '&' : '?')}platform={Uri.EscapeDataString(_options.Platform)}";
        using var request = new HttpRequestMessage(method, new Uri(_options.BaseUri, path + query));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) IsleLiveMap/1.8");
        request.Headers.Referrer = new Uri(_options.BaseUri, "studio");
        request.Headers.TryAddWithoutValidation("Cookie", _options.SessionCookieHeader.Trim());
        if (body is not null)
        {
            request.Headers.TryAddWithoutValidation("Origin", _options.BaseUri.GetLeftPart(UriPartial.Authority));
            request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized || (int)response.StatusCode is >= 300 and < 400)
        {
            throw new SbtcIslandAuthenticationException("Phiên SBTC Island đã hết hạn. Đăng nhập Steam lại để dùng vault.");
        }

        if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.NotFound && !path.StartsWith("api/studio/genes/status", StringComparison.Ordinal))
            response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
        if (payload is System.Text.Json.Nodes.JsonObject json) json["_http_status"] = (int)response.StatusCode;
        if (payload is SbtcVaultResponse vault && !vault.SignedIn && vault.Ok)
        {
            throw new SbtcIslandAuthenticationException("Phiên SBTC Island đã hết hạn. Đăng nhập Steam lại để dùng vault.");
        }

        return payload;
    }

    private static IslePilotOverlayGarageCommandDto ToCommand(SbtcCommandResponse? payload) => new()
    {
        Ok = payload?.Ok ?? false,
        Pending = payload?.Pending,
        Error = payload?.Message ?? payload?.Error
    };

    private static IslePilotOverlayGarageDinoDto ToGarageDino(SbtcVaultSlot slot)
    {
        var palette = PaletteFrom(slot);
        return new IslePilotOverlayGarageDinoDto
        {
            Id = slot.DinoId,
            Name = slot.Name,
            Species = FirstNonEmpty(slot.AssetSpecies, slot.Species, slot.SpeciesClass),
            Gender = FirstNonEmpty(slot.Gender, slot.Sex),
            Growth = Percent(slot.GrowthPercent),
            Health = VitalFraction(slot, "health"),
            Hunger = VitalFraction(slot, "hunger"),
            Thirst = VitalFraction(slot, "thirst"),
            Stamina = VitalFraction(slot, "stamina"),
            IsPrimeElder = slot.IsPrime,
            ParkedAt = slot.ParkedAt,
            Palette = palette,
            Payload = slot.Skin?.Recipe is { } recipe ? new()
                { Pattern = recipe.Pattern, Variation = recipe.Variation, Theme = recipe.Theme ?? 0, Palette = palette } : null,
            MutationEligible = slot.MutationCount is > 0,
            PickableMutations = []
        };
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static double? Percent(double? value) => value is { } percent ? percent / 100d : null;

    private static double? VitalFraction(SbtcVaultSlot slot, string key)
    {
        foreach (var vital in slot.Vitals ?? [])
        {
            if (string.Equals(vital.Key, key, StringComparison.OrdinalIgnoreCase) && vital.Known)
            {
                return Percent(vital.Percent);
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a palette out of whatever the site called it (palette, colors or skin) and
    /// accepts both hex strings and linear RGBA arrays, because the two feeds do not use
    /// the same representation.
    /// </summary>
    private static IslePilotOverlayGaragePaletteDto? PaletteFrom(SbtcVaultSlot slot) =>
        slot.Skin?.Recipe is { } recipe ? recipe.ToPalette() : PaletteFrom(slot.Palette ?? slot.Colors ?? slot.Skin);

    private static IslePilotOverlayGaragePaletteDto? PaletteFrom(SbtcPaletteParts? parts) =>
        parts is null
            ? null
            : new IslePilotOverlayGaragePaletteDto
            {
                Body = parts.Body?.ToHex(),
                Markings = parts.Markings?.ToHex(),
                Flank = parts.Flank?.ToHex(),
                Underbelly = parts.Underbelly?.ToHex(),
                Detail = parts.Detail1?.ToHex(),
                Display = parts.MaleDisplay?.ToHex(),
                Eyes = parts.Eyes?.ToHex(),
                Teeth = parts.Teeth?.ToHex(),
                Mouth = parts.Mouth?.ToHex(),
                Claws = parts.Claws?.ToHex()
            };

    private static string? PalettePart(SbtcPaletteParts? parts, string key) => key switch
    {
        "body" => parts?.Body?.ToHex(),
        "markings" => parts?.Markings?.ToHex(),
        "flank" => parts?.Flank?.ToHex(),
        "underbelly" => parts?.Underbelly?.ToHex(),
        "detail1" => parts?.Detail1?.ToHex(),
        "male_display" => parts?.MaleDisplay?.ToHex(),
        "eyes" => parts?.Eyes?.ToHex(),
        "teeth" => parts?.Teeth?.ToHex(),
        "mouth" => parts?.Mouth?.ToHex(),
        "claws" => parts?.Claws?.ToHex(),
        _ => null
    };

    internal sealed record SbtcVaultResponse
    {
        public bool Ok { get; init; }
        public bool SignedIn { get; init; }
        public bool? DbConfigured { get; init; }
        public bool? DbPresent { get; init; }
        public bool? StorageOn { get; init; }
        public string? Message { get; init; }
        public SbtcVaultOutcome? ParkOutcome { get; init; }
        public SbtcVaultOutcome? RedeemOutcome { get; init; }
        public IReadOnlyList<SbtcVaultSlot> List { get; init; } = [];
    }

    internal sealed record SbtcVaultSlot
    {
        public string? DinoId { get; init; }
        public string? Name { get; init; }
        public string? Species { get; init; }
        public string? SpeciesClass { get; init; }
        public string? AssetSpecies { get; init; }
        public string? Gender { get; init; }
        public string? Sex { get; init; }
        public double? GrowthPercent { get; init; }
        public string? LifeStage { get; init; }
        public bool? IsPrime { get; init; }
        public int? MutationCount { get; init; }
        [JsonConverter(typeof(SbtcEpochDateConverter))]
        public DateTimeOffset? ParkedAt { get; init; }
        public IReadOnlyList<SbtcVital>? Vitals { get; init; }
        public SbtcPaletteParts? Palette { get; init; }
        public SbtcPaletteParts? Colors { get; init; }
        public SbtcPaletteParts? Skin { get; init; }
    }

    internal sealed record SbtcVital
    {
        public string? Key { get; init; }
        public bool Known { get; init; }
        public double? Percent { get; init; }
    }

    private sealed class SbtcEpochDateConverter : JsonConverter<DateTimeOffset?>
    {
        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt64(out var seconds)
                && seconds is >= -62135596800 and <= 253402300799) return DateTimeOffset.FromUnixTimeSeconds(seconds);
            if (reader.TokenType == JsonTokenType.String && DateTimeOffset.TryParse(reader.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var parsed)) return parsed;
            reader.Skip();
            return null;
        }
        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            if (value is { } date) writer.WriteNumberValue(date.ToUnixTimeSeconds()); else writer.WriteNullValue();
        }
    }

    internal sealed record SbtcVaultOutcome
    {
        public string? Status { get; init; }
        public double? At { get; init; }
        public bool? Verified { get; init; }
        public string? Message { get; init; }
    }

    private sealed record SbtcCaveStatus
    {
        public string? Status { get; init; }
        public string? Detail { get; init; }
    }

    internal sealed record SbtcPaletteParts
    {
        public SbtcSkinRecipe? Recipe { get; init; }
        public SbtcColor? Body { get; init; }
        public SbtcColor? Markings { get; init; }
        public SbtcColor? Flank { get; init; }
        public SbtcColor? Underbelly { get; init; }
        public SbtcColor? Detail1 { get; init; }
        public SbtcColor? MaleDisplay { get; init; }
        public SbtcColor? Eyes { get; init; }
        public SbtcColor? Teeth { get; init; }
        public SbtcColor? Mouth { get; init; }
        public SbtcColor? Claws { get; init; }
    }

    /// <summary>A colour that may arrive as "#rrggbb" or as a linear RGBA array.</summary>
    internal sealed record SbtcColor
    {
        public string? Hex { get; init; }
        public IReadOnlyList<double>? Rgba { get; init; }

        public string? ToHex()
        {
            if (!string.IsNullOrWhiteSpace(Hex))
            {
                var value = Hex!.Trim();
                return value.StartsWith('#') ? value.ToUpperInvariant() : $"#{value.ToUpperInvariant()}";
            }

            if (Rgba is { Count: >= 3 })
            {
                return $"#{ToSrgbByte(Rgba[0]):X2}{ToSrgbByte(Rgba[1]):X2}{ToSrgbByte(Rgba[2]):X2}";
            }

            return null;
        }

        // Linear RGB (what the game stores) back to the sRGB byte the editor shows.
        private static byte ToSrgbByte(double linear)
        {
            var clamped = Math.Clamp(linear, 0d, 1d);
            var srgb = clamped <= 0.0031308
                ? clamped * 12.92
                : (1.055 * Math.Pow(clamped, 1d / 2.4d)) - 0.055;
            return (byte)Math.Round(Math.Clamp(srgb, 0d, 1d) * 255d);
        }
    }

    /// <summary>The site sends a part as "#rrggbb" in one feed and as linear RGBA in another.</summary>
    private sealed class SbtcColorConverter : JsonConverter<SbtcColor>
    {
        public override SbtcColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return new SbtcColor { Hex = reader.GetString() };
                case JsonTokenType.StartArray:
                    var values = new List<double>();
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.Number)
                        {
                            values.Add(reader.GetDouble());
                        }
                    }

                    return new SbtcColor { Rgba = values };
                case JsonTokenType.StartObject:
                    string? hex = null;
                    List<double>? rgba = null;
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                    {
                        if (reader.TokenType != JsonTokenType.PropertyName)
                        {
                            continue;
                        }

                        var name = reader.GetString();
                        reader.Read();
                        if (string.Equals(name, "hex", StringComparison.OrdinalIgnoreCase))
                        {
                            hex = reader.GetString();
                        }
                        else if (string.Equals(name, "rgba", StringComparison.OrdinalIgnoreCase) && reader.TokenType == JsonTokenType.StartArray)
                        {
                            rgba = [];
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            {
                                if (reader.TokenType == JsonTokenType.Number)
                                {
                                    rgba.Add(reader.GetDouble());
                                }
                            }
                        }
                    }

                    return new SbtcColor { Hex = hex, Rgba = rgba };
                default:
                    reader.Skip();
                    return new SbtcColor();
            }
        }

        public override void Write(Utf8JsonWriter writer, SbtcColor value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Hex ?? string.Empty);
    }

    internal sealed record SbtcDesignsResponse
    {
        public bool? Ok { get; init; }
        public bool? SignedIn { get; init; }
        public IReadOnlyList<SbtcDesign>? Designs { get; init; }
    }

    internal sealed record SbtcDesign
    {
        public int? Id { get; init; }
        public string? Name { get; init; }
        public string? Species { get; init; }
        public string? Sex { get; init; }
        public JsonElement? Recipe { get; init; }
        public int? Variation { get; init; }
        public int? Pattern { get; init; }
        public int? Theme { get; init; }
        public SbtcPaletteParts? Palette { get; init; }
        public SbtcPaletteParts? Colors { get; init; }
        public SbtcPaletteParts? Skin { get; init; }
    }

    internal sealed record SbtcPalettesResponse
    {
        public bool Ok { get; init; }
        public SortedDictionary<string, SbtcPaletteParts>? Palettes { get; init; }
    }

    internal sealed record SbtcCommandResponse
    {
        public bool Ok { get; init; }
        public bool? Pending { get; init; }
        public string? Message { get; init; }
        public string? Error { get; init; }
        public string? Id { get; init; }
    }

    internal sealed record SbtcRenameResponse
    {
        public bool Ok { get; init; }
        public string? Name { get; init; }
        public string? Message { get; init; }
    }
}
