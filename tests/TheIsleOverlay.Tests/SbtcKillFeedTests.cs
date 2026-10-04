using System.Net;
using System.Net.Http;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.Tests;

public sealed class SbtcKillFeedTests
{
    private const string Valid = """
        {"ok":true,"available":true,"gated":false,"species_list":["Pteranodon","Deinosuchus"],"more":true,"rows":[
        {"at":1790912979,"cause":"natural","killer_known":false,"victim_name":"Bạn Ptera","victim_species":"Pteranodon","victim_growth":78.4},
        {"at":1790913000,"cause":"pvp","killer_known":true,"killer_name":"Tester","killer_species":"Deinosuchus","killer_growth":98,"victim_name":"Victim","victim_species":"Pteranodon","victim_growth":80},
        {"at":-1,"cause":"pvp"}]}
        """;

    [Fact]
    public async Task Feed_ParsesPublicRowsAndFiltersWithTheServerSpeciesParameter()
    {
        var handler = new Handler((request, _) =>
        {
            Assert.Equal("/api/boards/species", request.RequestUri!.AbsolutePath);
            Assert.Equal("?species=Deinosuchus", request.RequestUri.Query);
            Assert.False(request.Headers.Contains("Cookie"));
            return Task.FromResult(Response(HttpStatusCode.OK, Valid));
        });
        var feed = await Client(handler).LoadAsync("Deinosuchus");
        Assert.True(feed.Available);
        Assert.True(feed.More);
        Assert.Equal(2, feed.Rows.Count);
        Assert.Equal("Tester", feed.Rows[0].KillerName);
        Assert.Equal(98d, feed.Rows[0].KillerGrowth);
        Assert.Equal("natural", feed.Rows[1].Cause);
        Assert.False(feed.Rows[1].KillerKnown);
        Assert.Equal(78.4d, feed.Rows[1].VictimGrowth);
        Assert.Equal(2, feed.Species.Count);
    }

    [Fact]
    public async Task Feed_RetriesServerFailuresBeforeReturningGoodData()
    {
        var calls = 0;
        var handler = new Handler((_, _) => Task.FromResult(++calls < 3
            ? Response(HttpStatusCode.ServiceUnavailable, "busy") : Response(HttpStatusCode.OK, Valid)));
        Assert.True((await Client(handler).LoadAsync()).Available);
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Feed_RetriesAnHtmlChallengeInsteadOfCallingItAnEmptyLog()
    {
        var calls = 0;
        var handler = new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            ++calls == 1 ? "<html>temporarily unavailable</html>" : Valid)));
        Assert.Equal(2, (await Client(handler).LoadAsync()).Rows.Count);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Feed_FailuresStopAfterThreeAttempts()
    {
        var calls = 0;
        var handler = new Handler((_, _) => { calls++; return Task.FromResult(Response(HttpStatusCode.BadGateway, "down")); });
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).LoadAsync());
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Feed_CancellationStopsRequestsWithoutRetrying()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = 0;
        var handler = new Handler(async (_, token) =>
        {
            calls++;
            cancellation.Cancel();
            await Task.Delay(1000, token);
            return Response(HttpStatusCode.OK, Valid);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).LoadAsync(cancellationToken: cancellation.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Feed_ReportsServerGatingWithoutPretendingTheFeedIsEmpty()
    {
        var handler = new Handler((_, _) => Task.FromResult(Response(HttpStatusCode.OK,
            """{"ok":true,"available":false,"gated":true,"reason":"membership_required"}""")));
        var feed = await Client(handler).LoadAsync();
        Assert.False(feed.Available);
        Assert.True(feed.Gated);
        Assert.Empty(feed.Rows);
    }

    private static SbtcKillFeedClient Client(Handler handler) => new(new HttpClient(handler), retryDelay: TimeSpan.Zero);
    private static HttpResponseMessage Response(HttpStatusCode status, string content) => new(status) { Content = new StringContent(content) };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
