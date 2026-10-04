using System.Net;
using System.Text;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.Tests;

public sealed class SbtcIslandVaultClientTests
{
    [Fact]
    public async Task GetVaultAsync_MapsTheSiteVaultOntoTheGarageShapeTheOverlaySpeaks()
    {
        var handler = new RoutingHandler(
            ("/api/vault", """
                {
                  "ok": true, "signed_in": true,
                  "list": [
                    {
                      "dino_id": "dino-7", "name": "Big Trike", "species_class": "Triceratops",
                      "gender": "female", "growth_percent": 49.2, "life_stage": "subadult",
                      "is_prime": false, "mutation_count": 1,
                      "parked_at": "2026-09-29T10:11:12Z",
                      "vitals": [
                        { "key": "health", "known": true, "percent": 67.75 },
                        { "key": "hunger", "known": true, "percent": 18.04 }
                      ],
                      "colors": {
                        "body": [0.40724, 0.215861, 0.0865, 1.0],
                        "markings": "#336699"
                      }
                    }
                  ]
                }
                """));

        var client = new SbtcIslandVaultClient(new HttpClient(handler), Options());

        var vault = await client.GetVaultAsync();

        var dino = Assert.Single(vault.Dinos);
        Assert.Equal("dino-7", dino.Id);
        Assert.Equal("Big Trike", dino.Name);
        Assert.Equal("Triceratops", dino.Species);
        Assert.Equal("female", dino.Gender);
        Assert.Equal(0.492d, dino.Growth!.Value, precision: 4);
        Assert.Equal(0.6775d, dino.Health!.Value, precision: 4);
        Assert.Equal(0.1804d, dino.Hunger!.Value, precision: 4);
        Assert.False(dino.IsPrimeElder);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 10, 11, 12, TimeSpan.Zero), dino.ParkedAt);

        // Linear RGBA becomes the hex the editor inputs use, and hex passes through.
        Assert.Equal("#AB8053", dino.Palette!.Body);
        Assert.Equal("#336699", dino.Palette.Markings);
    }

    [Fact]
    public async Task ApplyDesignAsync_PostsTheDesignIdToTheSlotsSkinEndpoint()
    {
        var handler = new RoutingHandler(("/api/vault/skin/dino-7", """{ "ok": true, "message": "Saved." }"""));
        var client = new SbtcIslandVaultClient(new HttpClient(handler), Options());

        var result = await client.ApplyDesignAsync("dino-7", "42");

        Assert.True(result.Ok);
        Assert.Contains("design_id", handler.LastBody);
        Assert.Contains("42", handler.LastBody);
        Assert.Equal("POST", handler.LastMethod);
    }

    [Fact]
    public async Task RenameAsync_ReturnsTheNameTheSiteAccepted()
    {
        var handler = new RoutingHandler(("/api/vault/name/dino-7", """{ "ok": true, "name": "Trike" }"""));
        var client = new SbtcIslandVaultClient(new HttpClient(handler), Options());

        var name = await client.RenameAsync("dino-7", "Trike");

        Assert.Equal("Trike", name);
    }

    [Fact]
    public async Task GetDesignsAsync_ReadsSavedDesignsWithTheirPalette()
    {
        var handler = new RoutingHandler(("/api/designs", """
            {
              "ok": true, "signed_in": true,
              "designs": [
                { "id": 3, "name": "Night", "species": "Triceratops", "theme": 1, "pattern": 2, "variation": 4,
                  "colors": { "body": "#112233", "eyes": [1.0, 0.5, 0.0, 1.0] } }
              ]
            }
            """));
        var client = new SbtcIslandVaultClient(new HttpClient(handler), Options());

        var designs = await client.GetDesignsAsync();

        var design = Assert.Single(designs.Drafts);
        Assert.Equal("3", design.Id);
        Assert.Equal("Night", design.Name);
        Assert.Equal(1, design.Theme);
        Assert.Equal("#112233", design.Palette!.Body);
        Assert.Equal("#FFBC00", design.Palette.Eyes);
    }

    [Fact]
    public async Task GetPalettesAsync_ReadsThePublicPaletteTable()
    {
        var handler = new RoutingHandler(("/api/palettes", """
            { "ok": true, "palettes": { "Triceratops": { "body": [0.40724, 0.215861, 0.0865, 1.0] } } }
            """));
        var client = new SbtcIslandVaultClient(new HttpClient(handler), Options());

        var palettes = await client.GetPalettesAsync();

        Assert.Equal("#AB8053", palettes["Triceratops"].Body);
    }

    [Fact]
    public async Task ParkAsync_UsesTheSitesParkEndpoint()
    {
        var handler = new RoutingHandler(("/api/vault/park", """{ "ok": true, "pending": true }"""));
        var client = new SbtcIslandVaultClient(new HttpClient(handler), Options());

        var command = await client.ParkAsync();

        Assert.True(command.Ok);
        Assert.True(command.Pending);
        Assert.Equal("/api/vault/park", handler.LastPath);
    }

    [Fact]
    public async Task ExpiredSession_IsReportedAsAnAuthenticationFailure()
    {
        var handler = new RoutingHandler(("/api/vault", """{ "ok": true, "signed_in": false, "list": [] }"""));
        var client = new SbtcIslandVaultClient(new HttpClient(handler), Options());

        await Assert.ThrowsAsync<SbtcIslandAuthenticationException>(() => client.GetVaultAsync());
    }

    private static SbtcIslandOptions Options() => new() { SessionCookieHeader = "session=abc" };

    private sealed class RoutingHandler(params (string Path, string Payload)[] routes) : HttpMessageHandler
    {
        public string LastPath { get; private set; } = string.Empty;
        public string LastMethod { get; private set; } = string.Empty;
        public string LastBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastPath = request.RequestUri?.AbsolutePath ?? string.Empty;
            LastMethod = request.Method.Method;
            LastBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            foreach (var (route, payload) in routes)
            {
                if (string.Equals(route, LastPath, StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("""{"ok":false,"error":"not_found"}""", Encoding.UTF8, "application/json")
            };
        }
    }
}
