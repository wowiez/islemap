using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.Sbtc;

// Recipes use picker RGB / 255, unlike the linear RGB in /api/palettes.
public sealed record SbtcSkinRecipe
{
    public int Pattern { get; init; }
    public int Variation { get; init; } = 8;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? ContractVersion { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Theme { get; init; }
    public double[]? Body { get; init; }
    public double[]? Markings { get; init; }
    public double[]? Flank { get; init; }
    public double[]? Underbelly { get; init; }
    public double[]? Detail1 { get; init; }
    public double[]? MaleDisplay { get; init; }
    public double[]? Eyes { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? Teeth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? Mouth { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? Claws { get; init; }

    public IslePilotOverlayGaragePaletteDto ToPalette() => new()
    {
        Body = Hex(Body), Markings = Hex(Markings), Flank = Hex(Flank), Underbelly = Hex(Underbelly),
        Detail = Hex(Detail1), Display = Hex(MaleDisplay), Eyes = Hex(Eyes),
        Teeth = Hex(Teeth), Mouth = Hex(Mouth), Claws = Hex(Claws)
    };

    private static string? Hex(double[]? values) => values is { Length: >= 3 } && values.Take(3).All(v => double.IsFinite(v) && v is >= 0 and <= 1)
        ? "#" + string.Concat(values.Take(3).Select(v => ((int)Math.Round(v * 255, MidpointRounding.AwayFromZero)).ToString("X2"))) : null;

    public static SbtcSkinRecipe FromPalette(IslePilotOverlayGaragePaletteDto palette, int pattern, int variation, int theme, bool advanced) => new()
    {
        Pattern = pattern, Variation = variation is 2 or 8 or 16 ? variation : 8,
        ContractVersion = advanced ? 2 : null, Theme = advanced ? theme : null,
        Body = Raw(palette.Body), Markings = Raw(palette.Markings), Flank = Raw(palette.Flank), Underbelly = Raw(palette.Underbelly),
        Detail1 = Raw(palette.Detail), MaleDisplay = Raw(palette.Display), Eyes = Raw(palette.Eyes),
        Teeth = advanced ? Raw(palette.Teeth) : null, Mouth = advanced ? Raw(palette.Mouth) : null, Claws = advanced ? Raw(palette.Claws) : null
    };

    private static double[] Raw(string? hex)
    {
        var value = hex?.Trim().TrimStart('#');
        if (value is not { Length: 6 } || !value.All(Uri.IsHexDigit)) throw new ArgumentException("Mã màu HEX không hợp lệ.");
        return Enumerable.Range(0, 3).Select(i => Math.Round(int.Parse(value.Substring(i * 2, 2), NumberStyles.HexNumber) / 255d, 5))
            .Append(1d).ToArray();
    }
}

public sealed record SbtcSkinAccess(bool Enabled, bool Advanced, string LiveSpecies, bool GenesEnabled, double? Cost, double? Balance, string Message);
public sealed record SbtcPreparedSkinApply(string Species, SbtcSkinRecipe Recipe, SbtcSkinAccess Access);
public sealed record SbtcSkinAttempt(string Intent, string RetryKey, string? ServerRequestId = null);
public sealed record SbtcVaultParkTerms(bool UsesCave, double? Cost, string? Currency, double? Days, string Message);
public interface ISbtcSkinAttemptStore
{
    SbtcSkinAttempt? Load();
    void Save(SbtcSkinAttempt? attempt);
}

public sealed partial class SbtcIslandVaultClient
{
    private readonly SemaphoreSlim _skinWriteLock = new(1, 1);
    private SbtcSkinAttempt? _skinAttempt;
    public ISbtcSkinAttemptStore? SkinAttemptStore { get; init; }
    private string? _cavePress;

    public async Task<SbtcVaultParkTerms> GetParkTermsAsync(CancellationToken cancellationToken = default)
    {
        var cave = await SendAsync<JsonObject>(HttpMethod.Get, "api/cave", null, cancellationToken);
        if (Bool(cave, "on") != true) return new(false, null, null, null, "Dino sẽ rời khỏi đảo và được cất vào kho SBTC.");
        if (Bool(cave, "ok") != true || Bool(cave, "unavailable") == true || Number(cave, "save_cost") is not { } cost || cost < 0 || Text(cave, "currency") is not { } currency)
            throw new InvalidOperationException("Chưa đọc được điều kiện và phí Cave. Kiểm tra lại trước khi cất Dino.");
        var days = Number(cave, "term_days");
        if (days is null or <= 0) throw new InvalidOperationException("Chưa đọc được thời hạn Cave. Kiểm tra lại trước khi cất Dino.");
        return new(true, cost, currency, days, $"Lưu vào Cave: {cost:0.##} {currency}. Thời hạn: {days:0.##} ngày. Dino sẽ rời khỏi đảo; slot hết hạn có thể bị xóa.");
    }

    public async Task<IslePilotOverlayGarageCommandDto> ParkPreparedAsync(SbtcVaultParkTerms prepared, CancellationToken cancellationToken = default)
    {
        var fresh = await GetParkTermsAsync(cancellationToken);
        if (fresh.UsesCave != prepared.UsesCave || fresh.Cost != prepared.Cost || fresh.Currency != prepared.Currency || fresh.Days != prepared.Days)
            return new() { Ok = false, Error = "Phí / thời hạn lưu kho đã thay đổi. Kiểm tra và xác nhận lại." };
        if (!fresh.UsesCave) return await ParkAsync(cancellationToken);
        _cavePress ??= Guid.NewGuid().ToString("N");
        var cave = await SendAsync<SbtcCommandResponse>(HttpMethod.Post, "api/cave/park", new { press = _cavePress }, cancellationToken);
        if (cave?.Ok == true) _cavePress = null;
        return ToCommand(cave) with { Pending = cave?.Ok == true, CommandId = cave?.Id is { } id ? "cave:" + id : null };
    }

    public async Task<byte[]> DownloadModelAssetAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        // Asset URLs cannot send the authenticated cookie to a different origin.
        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host != _options.BaseUri.Host || uri.Port != _options.BaseUri.Port
            || !uri.AbsolutePath.StartsWith("/assets/dino/", StringComparison.Ordinal))
            throw new ArgumentException("Đường dẫn model SBTC không hợp lệ.", nameof(uri));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("Cookie", _options.SessionCookieHeader.Trim());
        request.Headers.Referrer = new Uri(_options.BaseUri, "studio");
        request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) IsleLiveMap/1.8");
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<SbtcSkinAccess> GetSkinAccessAsync(CancellationToken cancellationToken = default)
    {
        var state = await GetStudioStateAsync(cancellationToken);
        var enabled = Bool(state, "enabled") == true && !(Number(state, "studio_min_tier") > 0 && Bool(state, "meets_studio") != true);
        var contract = state["skin_contract"] as JsonObject;
        var advanced = Bool(contract, "enabled") == true && Number(contract, "schema_version") == 2;
        var genes = await SendAsync<JsonObject>(HttpMethod.Get, "api/studio/genes/state", null, cancellationToken);
        // Fail closed if the wallet did not explicitly disable payment tracking.
        var genesEnabled = Bool(genes, "enabled") != false;
        var cost = genesEnabled ? Number(genes, "ordinary_cost") : null;
        var balance = genesEnabled ? Number(genes, "balance") : null;
        if (genesEnabled && (Bool(genes, "available") == false || cost is null || balance is null || balance < cost)) enabled = false;
        var message = !enabled ? Text(state, "message") ?? "Studio chưa cho phép áp dụng, hoặc tài khoản chưa đủ quyền / skin genes."
            : genesEnabled ? $"Phí đổi skin: {cost:0.##} skin genes · số dư: {balance:0.##}." : "Server cho phép áp dụng skin.";
        return new(enabled, advanced, Text(state, "live_species") ?? string.Empty, genesEnabled, cost, balance, message);
    }

    private async Task<JsonObject> GetStudioStateAsync(CancellationToken cancellationToken)
    {
        var state = await SendAsync<JsonObject>(HttpMethod.Get, "api/studio/apply/state", null, cancellationToken)
            ?? throw new HttpRequestException("Không đọc được trạng thái studio SBTC.");
        if (Bool(state, "signed_in") == false) throw new SbtcIslandAuthenticationException("Đăng nhập Steam SBTC lại để dùng studio.");
        if (Bool(state, "ok") != true) throw new HttpRequestException(Text(state, "message") ?? "Không đọc được trạng thái studio SBTC.");
        return state;
    }

    public async Task<SbtcPreparedSkinApply> PrepareSkinApplyAsync(string species, IslePilotOverlayGaragePaletteDto palette,
        int pattern, int variation, int theme, CancellationToken cancellationToken = default)
    {
        var access = await GetSkinAccessAsync(cancellationToken);
        if (!access.Enabled) throw new InvalidOperationException(access.Message);
        if (!string.Equals(SpeciesKey(species), SpeciesKey(access.LiveSpecies), StringComparison.Ordinal))
            throw new InvalidOperationException("Dino trong studio SBTC đã đổi. Làm mới dữ liệu rồi chọn lại skin đúng loài.");
        var advanced = await SupportsRecipeAsync(species, pattern, theme, access.Advanced, cancellationToken);
        if (access.Advanced && !advanced) throw new InvalidOperationException("Pattern / theme này chưa được server SBTC hỗ trợ.");
        return new(species, SbtcSkinRecipe.FromPalette(palette, pattern, variation, theme, advanced), access);
    }

    private async Task<bool> SupportsRecipeAsync(string species, int pattern, int theme, bool enabled, CancellationToken cancellationToken)
    {
        if (!enabled) return false;
        var manifest = await SendAsync<JsonObject>(HttpMethod.Get, "api/studio/skin-contract", null, cancellationToken);
        manifest = manifest?["manifest"] as JsonObject ?? manifest;
        var state = await SendAsync<JsonObject>(HttpMethod.Get, "api/studio/apply/state", null, cancellationToken);
        var effective = state?["skin_contract"] as JsonObject;
        if (Number(manifest, "schema_version") != 2 || Text(manifest, "build") is not { Length: > 0 } build ||
            build != Text(effective, "build") || Bool(effective, "enabled") != true)
            return false;
        var slots = (effective?["slots"] as JsonArray)?.Select(n => n?.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        if (slots is null || !new[] { "body", "markings", "flank", "underbelly", "detail1", "male_display", "eyes", "teeth", "mouth", "claws" }.All(slots.Contains))
            return false;
        var entry = (manifest?["species"] as JsonObject)?.FirstOrDefault(p => SpeciesKey(p.Key) == SpeciesKey(species)).Value as JsonObject;
        var patterns = entry?["patterns"] as JsonArray;
        var selected = patterns?.OfType<JsonObject>().FirstOrDefault(p => Number(p, "index") == pattern);
        return (selected?["themes"] as JsonArray)?.OfType<JsonObject>().Any(t => Number(t, "index") == theme) == true
            && (effective?["themes"] as JsonArray)?.Any(t => t?.GetValue<int>() == theme) == true;
    }

    public async Task<IslePilotOverlaySkinDraftDto> SaveSkinDesignAsync(string species, string name, IslePilotOverlayGaragePaletteDto palette,
        int pattern, int variation, int theme, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Tên skin không được để trống.", nameof(name));
        // Saving a private design does not spend genes or require a live dinosaur.
        // The studio contract determines its recipe shape independently of the wallet.
        var state = await GetStudioStateAsync(cancellationToken);
        var contract = state["skin_contract"] as JsonObject;
        var advertisedAdvanced = Bool(contract, "enabled") == true && Number(contract, "schema_version") == 2;
        var advanced = await SupportsRecipeAsync(species, pattern, theme, advertisedAdvanced, cancellationToken);
        if (advertisedAdvanced && !advanced) throw new InvalidOperationException("Pattern / theme này chưa được SBTC hỗ trợ.");
        var recipe = SbtcSkinRecipe.FromPalette(palette, pattern, variation, theme, advanced);
        var result = await SendAsync<JsonObject>(HttpMethod.Post, "api/designs", new { name = name.Trim(), species, recipe }, cancellationToken);
        if (Bool(result, "ok") != true) throw new HttpRequestException(Text(result, "message") ?? Text(result, "error") ?? "Không lưu được thiết kế SBTC.");
        var saved = result?["design"] as JsonObject;
        return new() { Id = Number(saved, "id")?.ToString(CultureInfo.InvariantCulture),
            Name = Text(saved, "name") ?? name.Trim(), Species = Text(saved, "species") ?? species,
            Palette = palette, Pattern = pattern, Variation = recipe.Variation, Theme = theme };
    }

    public async Task<IslePilotOverlaySkinApplyDto> ApplyPreparedSkinAsync(SbtcPreparedSkinApply prepared, CancellationToken cancellationToken = default)
    {
        await _skinWriteLock.WaitAsync(cancellationToken);
        try
        {
            var fresh = await GetSkinAccessAsync(cancellationToken);
            if (!fresh.Enabled || fresh.GenesEnabled != prepared.Access.GenesEnabled || fresh.Cost != prepared.Access.Cost
                || SpeciesKey(fresh.LiveSpecies) != SpeciesKey(prepared.Species))
                return new() { Ok = false, Error = "Trạng thái Dino / phí đổi skin đã thay đổi. Làm mới studio rồi gửi lại." };
            if (prepared.Recipe.ContractVersion == 2 && !await SupportsRecipeAsync(prepared.Species,
                prepared.Recipe.Pattern, prepared.Recipe.Theme ?? 0, fresh.Advanced, cancellationToken))
                return new() { Ok = false, Error = "Hợp đồng skin của server đã thay đổi. Làm mới studio rồi gửi lại." };
            var body = new JsonObject { ["recipe"] = JsonSerializer.SerializeToNode(prepared.Recipe, JsonOptions), ["confirm"] = true };
            var intent = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body.ToJsonString())));
            _skinAttempt ??= SkinAttemptStore?.Load();
            var hadPendingAttempt = _skinAttempt is not null;
            if (_skinAttempt is { } previous && previous.Intent != intent)
                return new() { Ok = false, Pending = true, Error = "Skin trước chưa được xác nhận. Bấm KIỂM TRA LỆNH SKIN trước khi gửi skin khác." };
            _skinAttempt ??= new(intent, Guid.NewGuid().ToString());
            SkinAttemptStore?.Save(_skinAttempt); // Persist before sending; a dropped response must not cause a second payment.
            body["request_id"] = _skinAttempt.RetryKey;
            var result = await SendAsync<JsonObject>(HttpMethod.Post, "api/studio/apply", body, cancellationToken);
            if (Bool(result, "ok") != true)
            {
                var status = Number(result, "_http_status");
                var rejected = !hadPendingAttempt && status is >= 400 and < 500 && status is not (408 or 409 or 429);
                if (rejected) { _skinAttempt = null; SkinAttemptStore?.Save(null); }
                return new() { Ok = false, Pending = !rejected, Error = Text(result, "message") ?? Text(result, "error") ?? "Chưa xác nhận được yêu cầu skin. Kiểm tra lệnh trước khi thử lại." };
            }
            var delivery = Text(result, "delivery_state");
            if (delivery is not null)
            {
                _skinAttempt = _skinAttempt with { ServerRequestId = Text(result, "request_id") };
                SkinAttemptStore?.Save(_skinAttempt);
                return DeliveryResult(delivery);
            }
            // Older server writers acknowledge via /apply/confirm, not the payment endpoint.
            return new() { Ok = true, Pending = true, Message = "Đã gửi skin. Đang chờ server xác nhận; bấm KIỂM TRA LỆNH SKIN." };
        }
        finally { _skinWriteLock.Release(); }
    }

    public async Task<IslePilotOverlaySkinApplyDto> CheckSkinDeliveryAsync(CancellationToken cancellationToken = default)
    {
        await _skinWriteLock.WaitAsync(cancellationToken);
        try
        {
            _skinAttempt ??= SkinAttemptStore?.Load();
            if (_skinAttempt is null) return new() { Ok = true, Message = "Không có yêu cầu skin đang chờ trong overlay." };
            var path = _skinAttempt.ServerRequestId is { Length: > 0 } id
                ? "api/studio/genes/status?request_id=" + Uri.EscapeDataString(id)
                : "api/studio/genes/status?retry_key=" + Uri.EscapeDataString(_skinAttempt.RetryKey);
            var result = await SendAsync<JsonObject>(HttpMethod.Get, path, null, cancellationToken);
            if (Text(result, "delivery_state") is { } delivery) return DeliveryResult(delivery);
            // Only use the legacy confirmation when genes are explicitly disabled.
            var access = await GetSkinAccessAsync(cancellationToken);
            if (!access.GenesEnabled)
            {
                var confirm = await SendAsync<JsonObject>(HttpMethod.Get, "api/studio/apply/confirm", null, cancellationToken);
                if (Bool(confirm, "ok") == true && Text(confirm, "state") == "applied") return DeliveryResult("succeeded");
                if (Bool(confirm, "ok") == true && Text(confirm, "state") == "not_applied") return DeliveryResult("refused");
            }
            return new() { Ok = true, Pending = true, Message = "Server chưa xác nhận skin. Giữ nguyên yêu cầu này để tránh trả phí lần hai." };
        }
        finally { _skinWriteLock.Release(); }
    }

    private IslePilotOverlaySkinApplyDto DeliveryResult(string state)
    {
        var terminal = state is "succeeded" or "refunded" or "refused";
        if (terminal) { _skinAttempt = null; SkinAttemptStore?.Save(null); }
        return new()
        {
            Ok = state is not ("refunded" or "refused"), Pending = !terminal,
            Message = state switch
            {
                "succeeded" => "Server đã xác nhận áp dụng skin trong game.",
                "refunded" => "Skin chưa áp dụng. Server đã hoàn lại skin genes.",
                "refused" => "Server từ chối skin. Không trừ skin genes.",
                "held" => "Server đang kiểm tra skin. Skin genes được giữ cho yêu cầu này; không gửi skin khác.",
                _ => "Đã gửi skin, đang chờ server xác nhận."
            },
            Error = state is "refunded" or "refused" ? "Skin chưa áp dụng." : null
        };
    }

    private static bool? Bool(JsonObject? obj, string key) => obj?[key] is JsonValue value && value.TryGetValue<bool>(out var result) ? result : null;
    private static double? Number(JsonObject? obj, string key)
    {
        if (obj?[key] is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var result) && double.IsFinite(result)) return result;
        return value.TryGetValue<int>(out var integer) ? integer : null;
    }
    private static string? Text(JsonObject? obj, string key) => obj?[key] is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;
    private static string SpeciesKey(string? species)
    {
        var value = species?.Trim() ?? string.Empty;
        if (value.StartsWith("BP_", StringComparison.OrdinalIgnoreCase)) value = value[3..];
        if (value.EndsWith("_C", StringComparison.OrdinalIgnoreCase)) value = value[..^2];
        return new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }
}
