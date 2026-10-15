using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TheIsleOverlay.IslePilot;

/// <summary>
/// Garage and skin commands of an IslePilot website (for example 3.sdvn.org) on the
/// player's website session, for hosts that switched the overlay API off. The garage
/// list only exists in the /garage page data, park/status/skin are JSON routes, and
/// restore is a Next.js Server Action whose id changes on every deploy.
/// </summary>
public sealed partial class IslePilotWebGarageClient
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseUri;
    private readonly string _slug;
    private readonly string _cookieHeader;
    private string? _restoreActionId;
    private string? _skinServerId;

    public IslePilotWebGarageClient(HttpClient httpClient, Uri baseUri, string slug, string playerCookie)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(baseUri);
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("A server slug is required.", nameof(slug));
        if (string.IsNullOrWhiteSpace(playerCookie) || playerCookie.Contains('\r') || playerCookie.Contains('\n'))
            throw new ArgumentException("The website session is invalid.", nameof(playerCookie));

        _httpClient = httpClient;
        _baseUri = baseUri;
        _slug = slug;
        _cookieHeader = "islepilot_player=" + playerCookie.Trim();
    }

    public async Task<IslePilotOverlayGarageDto> GetGarageAsync(CancellationToken cancellationToken = default) =>
        ParseGarage(await GetPageAsync("garage", cancellationToken));

    /// <summary>
    /// Parks the dino being played. Some servers make the player hold still for a few
    /// seconds first (pending + delaySec); a cancelled wait tells the server to stop.
    /// </summary>
    public async Task<IslePilotOverlayGarageCommandDto> ParkAsync(CancellationToken cancellationToken = default)
    {
        var start = await ParkStepAsync("start", cancellationToken);
        if (!start.Ok || start.Pending != true || start.DelaySec is not > 0) return start;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(start.DelaySec.Value, 60)), cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Best effort: the overlay may already be closing and disposing the client.
            try { await ParkStepAsync("cancel", CancellationToken.None); }
            catch (Exception) { }
            throw;
        }

        return await ParkStepAsync("finalize", cancellationToken);
    }

    public async Task<IslePilotOverlayGarageCommandDto> RestoreAsync(string dinoId, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(dinoId, nameof(dinoId));
        _restoreActionId ??= await FindActionIdAsync("restoreDino", cancellationToken)
            ?? throw new InvalidDataException("Không tìm thấy lệnh lấy Dino trên website. Có thể website vừa cập nhật; thử lại sau.");

        using var request = Request(HttpMethod.Post, "garage", "text/x-component");
        request.Headers.TryAddWithoutValidation("Next-Action", _restoreActionId);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new[] { _slug, dinoId }), Encoding.UTF8, "text/plain");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfSignedOut(response);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // A redeploy retired the cached action id; look it up again next time.
            _restoreActionId = null;
            throw new HttpRequestException("Website vừa cập nhật lệnh lấy Dino. Thử lại.");
        }

        response.EnsureSuccessStatusCode();
        return ParseActionResult<IslePilotOverlayGarageCommandDto>(body)
            ?? throw new InvalidDataException("Website không trả kết quả lệnh lấy Dino.");
    }

    public Task<IslePilotOverlayGarageCommandStatusDto> GetCommandStatusAsync(string commandId, CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(commandId, nameof(commandId));
        return GetJsonAsync<IslePilotOverlayGarageCommandStatusDto>(
            $"api/player/command-status?id={Uri.EscapeDataString(commandId)}", cancellationToken);
    }

    public Task<IslePilotOverlaySkinDraftsDto> GetSkinDraftsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<IslePilotOverlaySkinDraftsDto>(
            $"api/player/skin-drafts?slug={Uri.EscapeDataString(_slug)}", cancellationToken);

    /// <summary>Whether this account may set skins, read from the site's own skin page.</summary>
    public async Task<bool> CanSetSkinAsync(CancellationToken cancellationToken = default)
    {
        var target = ParseSkinTarget(await GetPageAsync("skin", cancellationToken), _slug);
        _skinServerId = target.ServerId ?? _skinServerId;
        return target.CanSetSkin && target.ServerId is not null;
    }

    public Task<IslePilotOverlaySkinApplyDto> ApplySkinAsync(
        string species,
        IslePilotOverlayGaragePaletteDto palette,
        bool female,
        int theme,
        int pattern,
        int variation,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(species, nameof(species));
        ArgumentNullException.ThrowIfNull(palette);
        return SetSkinAsync(
            IslePilotOverlaySkinSetPayloadDto.FromPalette(species, palette, female, theme, pattern, variation),
            cancellationToken);
    }

    /// <summary>Sends a saved design as is, glitch layers included.</summary>
    public Task<IslePilotOverlaySkinApplyDto> ApplySkinDraftAsync(
        string species,
        IslePilotOverlaySkinDraftPayloadDto draft,
        bool? female = null,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentifier(species, nameof(species));
        ArgumentNullException.ThrowIfNull(draft);
        var payload = IslePilotOverlaySkinSetPayloadDto.FromDraft(species, draft, female);
        // A glitch design must reach the game as glitch; never fall back to its flat colours.
        if (draft.RenderMode is "glitched" or "glitch" && payload.Glitch != true)
            throw new InvalidOperationException("Thiết kế glitch này thiếu dữ liệu lớp glitch. Mở lại trên web rồi lưu lại.");
        return SetSkinAsync(payload, cancellationToken);
    }

    private async Task<IslePilotOverlaySkinApplyDto> SetSkinAsync(
        IslePilotOverlaySkinSetPayloadDto payload,
        CancellationToken cancellationToken)
    {
        if (_skinServerId is null && !await CanSetSkinAsync(cancellationToken))
            throw new InvalidOperationException("Tài khoản này chưa được đổi skin trên website.");

        using var request = Request(HttpMethod.Post, "api/skin/set", "application/json");
        request.Content = JsonContent.Create(
            new IslePilotOverlaySkinApplyRequest(_skinServerId!, payload),
            options: IslePilotOverlayJson.Options);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfSignedOut(response);
        var result = TryDeserialize<IslePilotOverlaySkinApplyDto>(body);
        if (!response.IsSuccessStatusCode)
        {
            return (result ?? new IslePilotOverlaySkinApplyDto()) with
            {
                Ok = false,
                Error = result?.Error ?? $"Website từ chối skin ({(int)response.StatusCode})."
            };
        }

        return result ?? new IslePilotOverlaySkinApplyDto { Ok = true };
    }

    private Task<IslePilotOverlayGarageCommandDto> ParkStepAsync(string step, CancellationToken cancellationToken) =>
        PostJsonAsync<IslePilotOverlayGarageCommandDto>("api/player/garage/park", new { slug = _slug, step }, cancellationToken);

    internal async Task<string?> FindActionIdAsync(string actionName, CancellationToken cancellationToken)
    {
        var html = await GetPageAsync("garage", cancellationToken);
        foreach (var chunk in ChunkPaths().Matches(html).Select(match => match.Value).Distinct(StringComparer.Ordinal))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, chunk));
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) continue;
            var id = FindServerActionId(await response.Content.ReadAsStringAsync(cancellationToken), actionName);
            if (id is not null) return id;
        }

        return null;
    }

    private async Task<string> GetPageAsync(string path, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, path, "text/html");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        ThrowIfSignedOut(response);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        // Signed-out pages still answer 200 but offer the Steam login link instead.
        if (html.Contains("/api/player/steam/login", StringComparison.Ordinal))
            throw new IslePilotOverlayAuthenticationException("Phiên website đã hết hạn hoặc chưa đăng nhập.");
        return html;
    }

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, path, "application/json");
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        ThrowIfSignedOut(response);
        response.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<T>(await response.Content.ReadAsStringAsync(cancellationToken), IslePilotOverlayJson.Options)
            ?? throw new InvalidDataException("Website trả về dữ liệu rỗng.");
    }

    private async Task<T> PostJsonAsync<T>(string path, object body, CancellationToken cancellationToken) where T : class
    {
        using var request = Request(HttpMethod.Post, path, "application/json");
        request.Content = JsonContent.Create(body, options: IslePilotOverlayJson.Options);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        ThrowIfSignedOut(response);
        // Refusals (cooldown, wrong state) come back as JSON errors with a 4xx status.
        return TryDeserialize<T>(text)
            ?? throw new HttpRequestException($"Website không phản hồi đúng ({(int)response.StatusCode}).");
    }

    private HttpRequestMessage Request(HttpMethod method, string path, string accept)
    {
        var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true, NoStore = true };
        request.Headers.TryAddWithoutValidation("Cookie", _cookieHeader);
        request.Headers.TryAddWithoutValidation("Origin", _baseUri.GetLeftPart(UriPartial.Authority));
        request.Headers.Referrer = new Uri(_baseUri, "garage");
        return request;
    }

    private static void ThrowIfSignedOut(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.Unauthorized ||
            (int)response.StatusCode is >= 300 and < 400 &&
            response.Headers.Location?.OriginalString.Contains("login", StringComparison.OrdinalIgnoreCase) == true)
            throw new IslePilotOverlayAuthenticationException("Phiên website đã hết hạn hoặc chưa đăng nhập.");
    }

    private static T? TryDeserialize<T>(string text) where T : class
    {
        try { return JsonSerializer.Deserialize<T>(text, IslePilotOverlayJson.Options); }
        catch (JsonException) { return null; }
    }

    private static void ValidateIdentifier(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl))
            throw new ArgumentException("Giá trị không hợp lệ.", name);
    }

    /// <summary>Next.js streams page data as self.__next_f.push([1,"…"]) string pieces.</summary>
    internal static string FlightData(string html)
    {
        var builder = new StringBuilder();
        foreach (Match match in FlightChunks().Matches(html))
        {
            builder.Append(JsonSerializer.Deserialize<string>("\"" + match.Groups[1].Value + "\""));
        }

        return builder.ToString();
    }

    internal static IslePilotOverlayGarageDto ParseGarage(string html)
    {
        var dinos = new List<IslePilotOverlayGarageDinoDto>();
        bool? liveSwap = null;
        foreach (var card in FlightObjects(FlightData(html)).SelectMany(DinoCards))
        {
            liveSwap ??= card["liveSwap"]?.GetValueKind() == JsonValueKind.True;
            dinos.Add(ToDino(card));
        }

        return new IslePilotOverlayGarageDto
        {
            Settings = new IslePilotOverlayGarageSettingsDto { LiveSwap = liveSwap ?? false },
            Dinos = dinos
        };
    }

    internal static (bool CanSetSkin, string? ServerId) ParseSkinTarget(string html, string slug)
    {
        foreach (var node in FlightObjects(FlightData(html)).SelectMany(Descendants))
        {
            if (Text(node["serverId"]) is { } id && string.Equals(Text(node["slug"]), slug, StringComparison.Ordinal))
            {
                return (node["canSetSkin"]?.GetValueKind() == JsonValueKind.True, id);
            }
        }

        return (false, null);
    }

    /// <summary>A Server Action answers in RSC lines ("0:{…}\n1:{…}"); the result is the object with "ok".</summary>
    internal static T? ParseActionResult<T>(string rsc) where T : class
    {
        foreach (var node in FlightObjects(rsc))
        {
            if (node is JsonObject obj && obj.ContainsKey("ok"))
                return obj.Deserialize<T>(IslePilotOverlayJson.Options);
        }

        return null;
    }

    internal static string? FindServerActionId(string javascript, string actionName)
    {
        var match = Regex.Match(javascript,
            "createServerReference\\)\\(\"([0-9a-f]{40,})\"[^)]{0,200}?\"" + Regex.Escape(actionName) + "\"\\)");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static IEnumerable<JsonNode> FlightObjects(string flight)
    {
        foreach (var line in flight.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0 || colon + 1 >= line.Length || line[colon + 1] is not ('{' or '[')) continue;
            JsonNode? node;
            try { node = JsonNode.Parse(line[(colon + 1)..]); }
            catch (JsonException) { continue; }
            if (node is not null) yield return node;
        }
    }

    private static IEnumerable<JsonObject> Descendants(JsonNode node)
    {
        var stack = new Stack<JsonNode>();
        stack.Push(node);
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case JsonObject obj:
                    yield return obj;
                    foreach (var child in obj.Select(pair => pair.Value).OfType<JsonNode>()) stack.Push(child);
                    break;
                case JsonArray array:
                    foreach (var child in array.OfType<JsonNode>()) stack.Push(child);
                    break;
            }
        }
    }

    // A garage card is the client component props: { slug, dino: { id, state, … }, palette, liveSwap, … }.
    private static IEnumerable<JsonObject> DinoCards(JsonNode root) =>
        Descendants(root).Where(obj => obj["dino"] is JsonObject dino && Text(dino["id"]) is not null && Text(obj["slug"]) is not null);

    private static IslePilotOverlayGaragePaletteDto? Palette(JsonNode? node)
    {
        if (node is not JsonObject) return null;
        try { return node.Deserialize<IslePilotOverlayGaragePaletteDto>(IslePilotOverlayJson.Options); }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return null; }
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static IslePilotOverlayGarageDinoDto ToDino(JsonObject card)
    {
        var dino = (JsonObject)card["dino"]!;
        var state = dino["state"] as JsonObject;
        double? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue(out double number) && double.IsFinite(number) ? number : null;
        double? Ratio(string current, string max) =>
            Number(state?[current]) is { } value && Number(state?[max]) is { } limit && limit > 0 ? value / limit : null;
        // RSC marks dates as "$D2026-10-12T15:52:25.487Z".
        var parkedAt = Text(dino["parkedAt"])?.TrimStart('$', 'D');

        return new IslePilotOverlayGarageDinoDto
        {
            Id = Text(dino["id"]),
            Name = Text(dino["customName"]),
            Species = Text(dino["className"]) ?? Text(state?["species"]),
            Gender = Text(dino["gender"]),
            Growth = Number(dino["growth"]) ?? Number(state?["growth"]),
            Health = Number(state?["healthFrac"]),
            Hunger = Ratio("hunger", "maxHunger"),
            Thirst = Ratio("thirst", "maxThirst"),
            Stamina = Ratio("stamina", "maxStamina"),
            IsPrimeElder = state?["isPrimeElder"]?.GetValueKind() == JsonValueKind.True,
            ParkedAt = DateTimeOffset.TryParse(parkedAt, out var at) ? at : null,
            Palette = Palette(card["palette"]),
            MutationEligible = card["mutationEligible"]?.GetValueKind() == JsonValueKind.True
        };
    }

    [GeneratedRegex("self\\.__next_f\\.push\\(\\[1,\"((?:[^\"\\\\]|\\\\.)*)\"\\]\\)")]
    private static partial Regex FlightChunks();

    [GeneratedRegex("/_next/static/chunks/[A-Za-z0-9_.~\\-]+\\.js")]
    private static partial Regex ChunkPaths();
}
