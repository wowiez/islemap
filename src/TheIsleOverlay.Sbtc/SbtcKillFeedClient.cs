using System.Net.Http;
using System.Text.Json;

namespace TheIsleOverlay.Sbtc;

public sealed record SbtcKillFeedRow(DateTimeOffset At, string Cause, bool KillerKnown,
    string KillerName, string KillerSpecies, double? KillerGrowth,
    string VictimName, string VictimSpecies, double? VictimGrowth);

public sealed record SbtcKillFeed(bool Available, bool Gated, string? Reason,
    IReadOnlyList<string> Species, IReadOnlyList<SbtcKillFeedRow> Rows, bool More);

/// <summary>Read-only public SBTC kill log, with bounded retries for intermittent failures.</summary>
public sealed class SbtcKillFeedClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly TimeSpan _retryDelay;
    private readonly TimeSpan _timeout;

    public SbtcKillFeedClient(HttpClient http, Uri? baseUri = null, TimeSpan? retryDelay = null, TimeSpan? timeout = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _baseUri = baseUri ?? new Uri("https://sbtcislandd.com/");
        _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
        _timeout = timeout ?? TimeSpan.FromSeconds(8);
    }

    public async Task<SbtcKillFeed> LoadAsync(string? species = null, CancellationToken cancellationToken = default)
    {
        var path = "api/boards/species" + (string.IsNullOrWhiteSpace(species) ? string.Empty :
            "?species=" + Uri.EscapeDataString(species.Trim()));
        Exception? failure = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_timeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri, path));
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
                var dto = await JsonSerializer.DeserializeAsync<FeedDto>(stream, JsonOptions, timeout.Token).ConfigureAwait(false)
                    ?? throw new JsonException("Empty kill log response.");
                var rows = (dto.Rows ?? []).Where(row => row is not null).Select(ToRow)
                    .Where(row => row is not null).Select(row => row!).OrderByDescending(row => row.At).Take(100).ToArray();
                return new(dto.Ok && dto.Available, dto.Gated, dto.Reason,
                    (dto.SpeciesList ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => Clean(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray(), rows, dto.More);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { failure = new TimeoutException("SBTC kill log timed out."); }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException)
            { failure = ex; }
            if (attempt < 2) await Task.Delay(_retryDelay * (attempt + 1), cancellationToken).ConfigureAwait(false);
        }
        throw new HttpRequestException("Không tải được Kill Feed SBTC sau 3 lần thử.", failure);
    }

    private static SbtcKillFeedRow? ToRow(RowDto dto)
    {
        if (dto.At is not { } seconds || !double.IsFinite(seconds) || seconds <= 0d) return null;
        DateTimeOffset at;
        try { at = DateTimeOffset.UnixEpoch.AddSeconds(seconds); }
        catch (ArgumentOutOfRangeException) { return null; }
        return new(at, Clean(dto.Cause), dto.KillerKnown, Clean(dto.KillerName), Clean(dto.KillerSpecies),
            Growth(dto.KillerGrowth), Clean(dto.VictimName), Clean(dto.VictimSpecies), Growth(dto.VictimGrowth));
    }

    private static double? Growth(double? value) => value is { } n && double.IsFinite(n) && n is >= 0d and <= 100d ? n : null;
    private static string Clean(string? text)
    {
        var cleaned = string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length > 160 ? cleaned[..160] : cleaned;
    }

    private sealed record FeedDto
    {
        public bool Ok { get; init; }
        public bool Available { get; init; }
        public bool Gated { get; init; }
        public string? Reason { get; init; }
        public string[]? SpeciesList { get; init; }
        public RowDto[]? Rows { get; init; }
        public bool More { get; init; }
    }

    private sealed record RowDto
    {
        public double? At { get; init; }
        public string? Cause { get; init; }
        public bool KillerKnown { get; init; }
        public string? KillerName { get; init; }
        public string? KillerSpecies { get; init; }
        public double? KillerGrowth { get; init; }
        public string? VictimName { get; init; }
        public string? VictimSpecies { get; init; }
        public double? VictimGrowth { get; init; }
    }
}
