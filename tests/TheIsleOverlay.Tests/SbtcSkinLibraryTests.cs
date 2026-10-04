using System.Net;
using System.Text;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.Tests;

public sealed class SbtcSkinLibraryTests
{
    private const string Ordinary = """{"designs":[{"id":7,"name":"Night","species":"Ceratosaurus","recipe":{"variation":8,"body":[0.25,0.5,0,1]}}]}""";
    private const string Glitch = """{"ok":true,"designs":[{"id":7,"name":"Glow","recipe":{"variation":1.7,"body":[10000,-20,5,1]}}]}""";

    [Fact]
    public async Task LoadsBothPersonalLibrariesWithoutExportingOrWritingRecipes()
    {
        var handler = new Handler(Ordinary, Glitch);
        var library = await Client(handler).GetSkinLibraryAsync();
        Assert.Equal(2, library.Drafts.Count);
        Assert.Equal("Night", library.Drafts[0].Name);
        Assert.Equal("#408000", library.Drafts[0].Palette!.Body);
        Assert.Equal("Glow", library.Drafts[1].Name);
        Assert.Equal("glitch", library.Drafts[1].RenderMode);
        Assert.Null(library.Drafts[1].Palette);
        Assert.Null(library.Message);
        Assert.Equal(["/api/designs", "/api/glitchcreator/designs"], handler.Paths);
    }

    [Fact]
    public async Task KeepsValidSavedRowsWhenAnotherRecipeCannotBeDecodedAsHex()
    {
        var handler = new Handler("""
            {"designs":[
              {"id":1,"name":"Supported","recipe":{"variation":8,"body":[1,0,0,1]}},
              {"id":2,"name":"Fractional variation","recipe":{"variation":1.7}},
              {"id":3,"name":"Metadata only"}]}
            """, """{"ok":true,"designs":[]}""");
        var library = await Client(handler).GetSkinLibraryAsync();
        Assert.Equal(3, library.Drafts.Count);
        Assert.Equal("#FF0000", library.Drafts[0].Palette!.Body);
        Assert.Null(library.Drafts[1].Palette);
        Assert.Null(library.Drafts[2].Palette);
    }

    [Theory]
    [InlineData(404)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task ACreatorFailureDoesNotHideOrdinarySavedDesigns(int status)
    {
        var handler = new Handler(Ordinary, "{}", glitchStatus: status);
        var library = await Client(handler).GetSkinLibraryAsync();
        Assert.Equal("Night", Assert.Single(library.Drafts).Name);
        Assert.False(string.IsNullOrWhiteSpace(library.Message));
    }

    [Fact]
    public async Task AnOrdinaryLibraryFailureDoesNotHideCreatorSavedDesigns()
    {
        var handler = new Handler("{}", Glitch, ordinaryStatus: 503);
        var library = await Client(handler).GetSkinLibraryAsync();
        Assert.Equal("Glow", Assert.Single(library.Drafts).Name);
        Assert.Contains("màu thường", library.Message);
    }

    [Fact]
    public async Task OnlyReturnsAnEmptyLibraryAfterBothLibrariesAnswerSuccessfully()
    {
        var library = await Client(new Handler("""{"designs":[]}""", """{"ok":true,"designs":[]}""")).GetSkinLibraryAsync();
        Assert.Empty(library.Drafts);
        Assert.Null(library.Message);
    }

    [Fact]
    public async Task DoesNotReportACompleteEmptyLibraryWhenNeitherEndpointAnswers()
    {
        var handler = new Handler("{}", "{}", 503, 503);
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetSkinLibraryAsync());
    }

    [Fact]
    public async Task AnExpiredCreatorSessionCannotLookLikeAnEmptyLibrary()
    {
        var handler = new Handler(Ordinary, """{"ok":true,"signed_in":false,"designs":[]}""");
        await Assert.ThrowsAsync<SbtcIslandAuthenticationException>(() => Client(handler).GetSkinLibraryAsync());
    }

    [Fact]
    public async Task MalformedCreatorResponseKeepsTheOrdinaryLibraryVisibleWithAWarning()
    {
        var handler = new Handler(Ordinary, """{"ok":false,"message":"creator unavailable"}""");
        var library = await Client(handler).GetSkinLibraryAsync();
        Assert.Single(library.Drafts);
        Assert.Contains("creator unavailable", library.Message);
    }

    [Fact]
    public async Task CancellationIsPropagatedWithoutReturningAnEmptyLibrary()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Client(new Handler(Ordinary, Glitch)).GetSkinLibraryAsync(cancellation.Token));
    }

    private static SbtcIslandVaultClient Client(Handler handler) => new(new HttpClient(handler),
        new SbtcIslandOptions { SessionCookieHeader = "session=synthetic-test-only" });

    private sealed class Handler(string ordinary, string glitch, int ordinaryStatus = 200, int glitchStatus = 200) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(HttpMethod.Get, request.Method);
            var path = request.RequestUri!.PathAndQuery;
            Paths.Add(path);
            Assert.Contains(path, new[] { "/api/designs", "/api/glitchcreator/designs" });
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)(path == "/api/designs" ? ordinaryStatus : glitchStatus))
            { Content = new StringContent(path == "/api/designs" ? ordinary : glitch, Encoding.UTF8, "application/json") });
        }
    }
}
