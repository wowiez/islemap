using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using TheIsleOverlay.IslePilot;
using TheIsleOverlay.Sbtc;

namespace TheIsleOverlay.Tests;

public sealed class SbtcSkinStudioTests
{
    private const string State = """{"ok":true,"signed_in":true,"enabled":true,"live_species":"Ceratosaurus","skin_contract":{"enabled":false}}""";
    private const string Wallet = """{"enabled":true,"available":true,"ordinary_cost":2,"balance":10}""";
    private static IslePilotOverlayGaragePaletteDto Palette => new()
    {
        Body = "#808000", Markings = "#336699", Flank = "#26302C", Underbelly = "#D9E1DC",
        Detail = "#5E7A6E", Display = "#8FB0A0", Eyes = "#E3EFE8", Teeth = "#EEEEEE", Mouth = "#885555", Claws = "#111111"
    };

    [Fact]
    public void Recipe_UsesRawPickerRgbAndSupportedVariation()
    {
        var recipe = SbtcSkinRecipe.FromPalette(Palette, 0, 0, 0, advanced: false);
        Assert.Equal([0.50196, 0.50196, 0, 1], recipe.Body!);
        Assert.Equal(8, recipe.Variation);
        Assert.Null(recipe.ContractVersion);
        Assert.Null(recipe.Teeth);
        Assert.Equal("#808000", recipe.ToPalette().Body);
    }

    [Theory]
    [InlineData("not-a-colour")]
    [InlineData("#FFF")]
    [InlineData("#11223Z")]
    public void Recipe_RejectsInvalidColorInsteadOfSilentlySendingGray(string color) =>
        Assert.Throws<ArgumentException>(() => SbtcSkinRecipe.FromPalette(Palette with { Body = color }, 0, 8, 0, false));

    [Fact]
    public async Task Designs_ReadRecipeRgbAndPatternWithoutLinearConversion()
    {
        var handler = new Handler((path, _) => path == "/api/designs" ? """
            {"ok":true,"designs":[{"id":9,"name":"Night","species":"Ceratosaurus",
              "recipe":{"pattern":2,"variation":16,"contract_version":2,"theme":1,"body":[0.5,0.5,0,1],"eyes":[1,0.5,0,1]}}]}
            """ : null);
        var draft = Assert.Single((await Client(handler).GetDesignsAsync()).Drafts);
        Assert.Equal("#808000", draft.GetPalette()!.Body);
        Assert.Equal("#FF8000", draft.GetPalette()!.Eyes);
        Assert.Equal(2, draft.GetPayload()!.Pattern);
        Assert.Equal(16, draft.GetPayload()!.Variation);
    }

    [Fact]
    public async Task SaveDesign_UsesTheRealStudioRecipeAndNoPlatformQuery()
    {
        var handler = new Handler((path, _) => path == "/api/designs" ? """{"ok":true,"design":{"id":9}}""" : null);
        await Client(handler).SaveSkinDesignAsync("Ceratosaurus", "Night", Palette, 0, 8, 0);
        var request = Assert.Single(handler.Writes);
        Assert.Equal("/api/designs", request.Uri.PathAndQuery);
        Assert.Equal("Night", request.Body!["name"]!.GetValue<string>());
        Assert.Equal("Ceratosaurus", request.Body["species"]!.GetValue<string>());
        Assert.Equal(0.50196, request.Body["recipe"]!["body"]![0]!.GetValue<double>());
        Assert.Null(request.Body["recipe"]!["teeth"]);
    }

    [Fact]
    public async Task SaveDesign_RoundTripsIntoTheServerLibraryWithoutDependingOnGenes()
    {
        JsonObject? saved = null;
        var handler = new Handler((path, body) =>
        {
            if (path == "/api/studio/genes/state") throw new HttpRequestException("Synthetic wallet unavailable");
            if (path != "/api/designs") return null;
            if (body is null) return new JsonObject
            {
                ["designs"] = saved is null ? new JsonArray() : new JsonArray(saved.DeepClone())
            }.ToJsonString();
            saved = new JsonObject { ["id"] = 9, ["name"] = body["name"]!.DeepClone(),
                ["species"] = body["species"]!.DeepClone(), ["recipe"] = body["recipe"]!.DeepClone() };
            return new JsonObject { ["ok"] = true, ["design"] = saved.DeepClone() }.ToJsonString();
        });
        var client = Client(handler);
        Assert.Empty((await client.GetDesignsAsync()).Drafts);
        var created = await client.SaveSkinDesignAsync("Pteranodon", "  Sunset  ", Palette, 2, 16, 0);
        var listed = Assert.Single((await client.GetDesignsAsync()).Drafts);
        Assert.Equal("9", created.Id);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("Sunset", listed.Name);
        Assert.Equal("Pteranodon", listed.GetSpecies());
        Assert.Equal(Palette.Body, listed.GetPalette()!.Body);
        Assert.Equal(2, listed.GetPayload()!.Pattern);
        Assert.Equal(16, listed.GetPayload()!.Variation);
        Assert.Equal("/api/designs", Assert.Single(handler.Writes).Uri.PathAndQuery);
        Assert.DoesNotContain(handler.Reads, uri => uri.AbsolutePath.Contains("/genes/"));
    }

    [Fact]
    public async Task SaveDesign_ReportsLibraryRefusalInsteadOfClaimingItWasSaved()
    {
        var handler = new Handler((path, _) => path == "/api/designs" ? """{"ok":false,"error":"library_full"}""" : null);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Client(handler).SaveSkinDesignAsync("Ceratosaurus", "Sunset", Palette, 0, 8, 0));
        Assert.Contains("library_full", error.Message);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"ok\":false,\"designs\":[]}")]
    [InlineData("{\"designs\":null}")]
    public async Task Designs_RejectsFailedOrMissingLibraryInsteadOfDisplayingAnEmptySuccess(string json)
    {
        var handler = new Handler((path, _) => path == "/api/designs" ? json : null);
        await Assert.ThrowsAsync<HttpRequestException>(() => Client(handler).GetDesignsAsync());
    }

    [Fact]
    public async Task Designs_ReportsAnExpiredStudioSession()
    {
        var handler = new Handler((path, _) => path == "/api/designs" ? """{"signed_in":false,"designs":[]}""" : null);
        await Assert.ThrowsAsync<SbtcIslandAuthenticationException>(() => Client(handler).GetDesignsAsync());
    }

    [Theory]
    [InlineData("{\"enabled\":true,\"available\":true,\"ordinary_cost\":20,\"balance\":10}")]
    [InlineData("{\"enabled\":true,\"available\":false}")]
    [InlineData("{}")]
    public async Task Prepare_BlocksInsufficientOrUnknownWalletWithoutWriting(string wallet)
    {
        var handler = new Handler((path, _) => path == "/api/studio/genes/state" ? wallet : null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0));
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Prepare_BlocksDifferentLiveSpeciesWithoutWriting()
    {
        var handler = new Handler();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).PrepareSkinApplyAsync("Triceratops", Palette, 0, 8, 0));
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Prepare_MatchesBlueprintSpeciesWithCanonicalLiveSpecies()
    {
        var handler = new Handler();
        var prepared = await Client(handler).PrepareSkinApplyAsync("BP_Ceratosaurus_C", Palette, 0, 8, 0);
        Assert.Equal("Ceratosaurus", prepared.Access.LiveSpecies);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Prepare_UsesAdvancedSlotsOnlyWhenEffectiveContractMatchesManifest()
    {
        var handler = AdvancedHandler("build-1");
        var prepared = await Client(handler).PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0);
        Assert.Equal(2, prepared.Recipe.ContractVersion);
        Assert.Equal(0, prepared.Recipe.Theme);
        Assert.NotNull(prepared.Recipe.Teeth);
    }

    [Fact]
    public async Task Prepare_RejectsContractBuildMismatchWithoutWriting()
    {
        var handler = AdvancedHandler("different-build");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0));
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Prepare_AcceptsWrappedManifestAndRequiresItsBuild()
    {
        var handler = new Handler((path, _) => path switch
        {
            "/api/studio/apply/state" => """{"ok":true,"signed_in":true,"enabled":true,"live_species":"Ceratosaurus","skin_contract":{"schema_version":2,"enabled":true,"build":"test-build","themes":[0],"slots":["body","markings","flank","underbelly","detail1","male_display","eyes","teeth","mouth","claws"]}}""",
            "/api/studio/skin-contract" => """{"ok":true,"manifest":{"schema_version":2,"build":"test-build","species":{"Ceratosaurus":{"patterns":[{"index":0,"themes":[{"index":0}]}]}}}}""",
            _ => null
        });
        var prepared = await Client(handler).PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0);
        Assert.Equal(2, prepared.Recipe.ContractVersion);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Prepare_RejectsAdvancedContractWithNoBuild()
    {
        var handler = new Handler((path, _) => path switch
        {
            "/api/studio/apply/state" => """{"ok":true,"signed_in":true,"enabled":true,"live_species":"Ceratosaurus","skin_contract":{"schema_version":2,"enabled":true,"themes":[0],"slots":["body","markings","flank","underbelly","detail1","male_display","eyes","teeth","mouth","claws"]}}""",
            "/api/studio/skin-contract" => """{"schema_version":2,"species":{"Ceratosaurus":{"patterns":[{"index":0,"themes":[{"index":0}]}]}}}""",
            _ => null
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client(handler).PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0));
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Apply_RechecksPriceBeforeSendingTheConfirmedRequest()
    {
        var changed = false;
        var handler = new Handler((path, _) => path == "/api/studio/genes/state" && changed
            ? """{"enabled":true,"available":true,"ordinary_cost":3,"balance":10}""" : null);
        var client = Client(handler);
        var prepared = await client.PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0);
        changed = true;
        Assert.False((await client.ApplyPreparedSkinAsync(prepared)).Accepted);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task Apply_PendingIsNotReportedAsAppliedAndRetryKeepsTheSamePaymentKey()
    {
        var handler = new Handler((path, _) => path == "/api/studio/apply" ? """{"ok":true,"delivery_state":"pending","request_id":"server-request"}""" : null);
        var store = new AttemptStore();
        var client = Client(handler, store);
        var prepared = await client.PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0);
        var pending = await client.ApplyPreparedSkinAsync(prepared);
        Assert.True(pending.Pending);
        Assert.DoesNotContain("đã xác nhận", pending.Message!);
        var restarted = Client(handler, store);
        await restarted.ApplyPreparedSkinAsync(prepared);
        Assert.Equal(2, handler.Writes.Count);
        Assert.Equal(handler.Writes[0].Body!["request_id"]!.GetValue<string>(), handler.Writes[1].Body!["request_id"]!.GetValue<string>());
        Assert.True(handler.Writes[0].Body!["confirm"]!.GetValue<bool>());
        Assert.Null(handler.Writes[0].Body!["sex"]);
    }

    [Fact]
    public async Task Apply_LostResponsePersistsKeyAndBlocksADifferentSkin()
    {
        var handler = new Handler((path, _) => path == "/api/studio/apply" ? throw new HttpRequestException("Synthetic dropped response") : null);
        var store = new AttemptStore();
        var client = Client(handler, store);
        var prepared = await client.PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.ApplyPreparedSkinAsync(prepared));
        Assert.NotNull(store.Attempt);
        var other = await client.PrepareSkinApplyAsync("Ceratosaurus", Palette with { Body = "#FF0000" }, 0, 8, 0);
        Assert.False((await Client(handler, store).ApplyPreparedSkinAsync(other)).Accepted);
        Assert.Single(handler.Writes);
    }

    [Theory]
    [InlineData("succeeded", true)]
    [InlineData("refunded", false)]
    [InlineData("refused", false)]
    public async Task Delivery_TerminalResultClearsPendingReference(string state, bool accepted)
    {
        var handler = new Handler((path, _) => path switch
        {
            "/api/studio/apply" => """{"ok":true,"delivery_state":"held","request_id":"server-request"}""",
            "/api/studio/genes/status" => $$"""{"ok":true,"delivery_state":"{{state}}"}""",
            _ => null
        });
        var store = new AttemptStore();
        var client = Client(handler, store);
        await client.ApplyPreparedSkinAsync(await client.PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0));
        var result = await client.CheckSkinDeliveryAsync();
        Assert.Equal(accepted, result.Accepted);
        Assert.False(result.Pending);
        Assert.Null(store.Attempt);
        Assert.Contains("request_id=server-request", handler.Reads.Last().PathAndQuery);
        Assert.Single(handler.Writes);
    }

    [Fact]
    public async Task Apply_Business403IsARefusalAndDoesNotExpireTheSteamSession()
    {
        var handler = new Handler((path, _) => path == "/api/studio/apply" ? """{"ok":false,"message":"Not enough genes"}""" : null)
        { ApplyStatus = HttpStatusCode.Forbidden };
        var store = new AttemptStore();
        var client = Client(handler, store);
        var result = await client.ApplyPreparedSkinAsync(await client.PrepareSkinApplyAsync("Ceratosaurus", Palette, 0, 8, 0));
        Assert.False(result.Accepted);
        Assert.False(result.Pending);
        Assert.Equal("Not enough genes", result.Error);
        Assert.Null(store.Attempt);
    }

    [Fact]
    public async Task Studio_SignedOutIsAnAuthenticationFailure()
    {
        var handler = new Handler((path, _) => path == "/api/studio/apply/state" ? """{"ok":true,"signed_in":false}""" : null);
        await Assert.ThrowsAsync<SbtcIslandAuthenticationException>(() => Client(handler).GetSkinAccessAsync());
    }

    [Fact]
    public async Task Vault_MapsEpochDateCanonicalAssetSpeciesAndRawSkinRecipe()
    {
        var handler = new Handler((path, _) => path == "/api/vault" ? """
            {"ok":true,"signed_in":true,"list":[{"dino_id":"synthetic-dino","species":"Cera","species_class":"BP_Ceratosaurus_C",
              "asset_species":"Ceratosaurus","parked_at":1750000000,"skin":{"recipe":{"pattern":2,"variation":8,"body":[0.5,0.5,0,1]}}}]}
            """ : null);
        var vault = await Client(handler).GetVaultAsync();
        var dino = Assert.Single(vault.Dinos);
        Assert.Equal("Ceratosaurus", dino.Species);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1750000000), dino.ParkedAt);
        Assert.Equal("#808000", dino.Palette!.Body);
        Assert.Equal(2, dino.Payload!.Pattern);
        Assert.False(vault.Settings!.LiveSwap);
    }

    [Theory]
    [InlineData("done", true, 0, "done")]
    [InlineData("done", false, 0, "pending")]
    [InlineData("done", true, -100, "pending")]
    [InlineData("failed", true, 0, "failed")]
    public async Task Vault_ParkWaitsForAFreshVerifiedServerOutcome(string outcome, bool verified, int ageOffset, string expected)
    {
        var payload = new JsonObject
        {
            ["ok"] = true, ["signed_in"] = true,
            ["park_outcome"] = new JsonObject { ["status"] = outcome, ["verified"] = verified, ["at"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ageOffset }
        }.ToJsonString();
        var handler = new Handler((path, _) => path switch
        {
            "/api/vault/park" => """{"ok":true}""",
            "/api/vault" => payload,
            _ => null
        });
        var client = Client(handler);
        var command = await client.ParkAsync();
        Assert.True(command.Pending);
        Assert.Equal(expected, (await client.GetCommandStatusAsync(command.CommandId!)).Status);
        Assert.Single(handler.Writes);
    }

    [Fact]
    public async Task Vault_RedeemUsesDinoIdAndWaitsForDelivery()
    {
        var handler = new Handler((path, _) => path == "/api/vault/redeem" ? """{"ok":true}""" : null);
        var command = await Client(handler).RestoreAsync("synthetic-dino");
        Assert.True(command.Pending);
        var request = Assert.Single(handler.Writes);
        Assert.Equal("/api/vault/redeem?platform=steam", request.Uri.PathAndQuery);
        Assert.Equal("synthetic-dino", request.Body!["dino_id"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cave_ChangedStorageFeeRequiresAnotherConfirmation()
    {
        var cost = 100;
        var handler = new Handler((path, _) => path == "/api/cave"
            ? new JsonObject { ["ok"] = true, ["on"] = true, ["save_cost"] = cost, ["currency"] = "points", ["term_days"] = 7 }.ToJsonString() : null);
        var client = Client(handler);
        var terms = await client.GetParkTermsAsync();
        Assert.Contains("100", terms.Message);
        cost = 200;
        Assert.False((await client.ParkPreparedAsync(terms)).Ok);
        Assert.Empty(handler.Writes);
    }

    [Fact]
    public async Task CaveBridge_DoesNotChargeWithoutReviewingCaveTerms()
    {
        var handler = new Handler((path, _) => path == "/api/vault/park" ? """{"ok":false,"error":"cave_bridge"}""" : null);
        var result = await Client(handler).ParkAsync();
        Assert.False(result.Ok);
        Assert.Single(handler.Writes);
        Assert.DoesNotContain(handler.Writes, request => request.Uri.AbsolutePath == "/api/cave/park");
    }

    [Theory]
    [InlineData("https://evil.example/assets/dino/Ceratosaurus/mesh.glb")]
    [InlineData("http://sbtcislandd.com/assets/dino/Ceratosaurus/mesh.glb")]
    [InlineData("https://sbtcislandd.com/api/admin/players")]
    public async Task ModelAssets_NeverSendSessionCookieToAnotherOriginOrRoute(string uri)
    {
        var handler = new Handler();
        await Assert.ThrowsAsync<ArgumentException>(() => Client(handler).DownloadModelAssetAsync(new(uri)));
        Assert.Empty(handler.Reads);
    }

    [Fact]
    public async Task Preview_ReadsContractAssetsAndMeasuredUtilityChannels()
    {
        var handler = new Handler((path, _) => path == "/api/studio/skin-contract" ? """
            {"schema_version":2,"build":"24664709","species":{"Pteranodon":{
                "patterns":[{"index":2,"preview_asset":"/assets/dino/Pteranodon/pattern_special.png"}],
                "material":{"preview_asset":"assets_current/utility.webp", "preview_channel_mapping":{"teeth":"b","mouth":"r","claws":"g"}}
            }}}
            """ : null);
        var result = await Client(handler).GetSkinPreviewAsync("BP_Pteranodon_C", 2);
        Assert.Equal("pattern_special.png", result.PatternAsset);
        Assert.Equal("utility.webp", result.UtilityAsset);
        Assert.Equal("b", result.UtilityChannels["teeth"]);
        Assert.Equal("r", result.UtilityChannels["mouth"]);
        Assert.Equal("g", result.UtilityChannels["claws"]);
        Assert.Empty(handler.Writes);
    }

    [Theory]
    [InlineData("https://evil.example/mask.png")]
    [InlineData("../mask.png")]
    [InlineData("Ceratosaurus/mask.png")]
    [InlineData("/Pteranodon/mask.png")]
    [InlineData("mask.js")]
    [InlineData("mask.png?redirect=external")]
    public async Task Preview_RejectsAssetsOutsideTheCurrentSpecies(string asset)
    {
        var handler = new Handler((path, _) => path == "/api/studio/skin-contract" ? new JsonObject
        {
            ["schema_version"] = 2, ["build"] = "test-build", ["species"] = new JsonObject
            {
                ["Pteranodon"] = new JsonObject
                {
                    ["patterns"] = new JsonArray(new JsonObject { ["index"] = 0, ["preview_asset"] = asset }),
                    ["material"] = new JsonObject { ["preview_asset"] = asset,
                        ["preview_channel_mapping"] = new JsonObject { ["teeth"] = "r" } }
                }
            }
        }.ToJsonString() : null);
        var result = await Client(handler).GetSkinPreviewAsync("Pteranodon", 0);
        Assert.Null(result.PatternAsset);
        Assert.Null(result.UtilityAsset);
        Assert.Empty(result.UtilityChannels);
    }

    [Fact]
    public async Task Preview_DropsUnverifiedSlotInsteadOfPaintingTheWrongRegion()
    {
        var handler = new Handler((path, _) => path == "/api/studio/skin-contract" ? """
            {"schema_version":2,"build":"24664709","species":{"Herrerasaurus":{
                "material":{"preview_asset":"utility.png", "preview_channel_mapping":{"mouth":"r","claws":"b"}}
            }}}
            """ : null);
        var result = await Client(handler).GetSkinPreviewAsync("Herrerasaurus", 0);
        Assert.False(result.UtilityChannels.ContainsKey("mouth"));
        Assert.Equal("b", result.UtilityChannels["claws"]);
    }

    [Fact]
    public async Task Preview_RejectsDuplicateChannelAssignments()
    {
        var handler = new Handler((path, _) => path == "/api/studio/skin-contract" ? """
            {"schema_version":2,"build":"test-build","species":{"Pteranodon":{
                "material":{"preview_asset":"utility.png", "preview_channel_mapping":{"teeth":"r","mouth":"r"}}
            }}}
            """ : null);
        var result = await Client(handler).GetSkinPreviewAsync("Pteranodon", 0);
        Assert.Null(result.UtilityAsset);
        Assert.Empty(result.UtilityChannels);
    }

    private static Handler AdvancedHandler(string build) => new((path, _) => path switch
    {
        "/api/studio/apply/state" => """{"ok":true,"signed_in":true,"enabled":true,"live_species":"Ceratosaurus","skin_contract":{"schema_version":2,"enabled":true,"build":"build-1","themes":[0],"slots":["body","markings","flank","underbelly","detail1","male_display","eyes","teeth","mouth","claws"]}}""",
        "/api/studio/skin-contract" => """{"schema_version":2,"build":"BUILD","species":{"Ceratosaurus":{"patterns":[{"index":0,"themes":[{"index":0}]}]}}}""".Replace("BUILD", build),
        _ => null
    });
    private static SbtcIslandVaultClient Client(Handler handler, AttemptStore? store = null) => new(new HttpClient(handler),
        new SbtcIslandOptions { SessionCookieHeader = "session=synthetic-test-only" }) { SkinAttemptStore = store };
    private sealed class AttemptStore : ISbtcSkinAttemptStore
    {
        public SbtcSkinAttempt? Attempt { get; private set; }
        public SbtcSkinAttempt? Load() => Attempt;
        public void Save(SbtcSkinAttempt? attempt) => Attempt = attempt;
    }
    private sealed class Handler(Func<string, JsonObject?, string?>? route = null) : HttpMessageHandler
    {
        public HttpStatusCode ApplyStatus { get; init; } = HttpStatusCode.OK;
        public List<(Uri Uri, JsonObject? Body)> Writes { get; } = [];
        public List<Uri> Reads { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken))!.AsObject();
            if (request.Method != HttpMethod.Get) Writes.Add((uri, body)); else Reads.Add(uri);
            var json = route?.Invoke(uri.AbsolutePath, body) ?? (uri.AbsolutePath switch
            {
                "/api/studio/apply/state" => State,
                "/api/studio/genes/state" => Wallet,
                _ => "{}"
            });
            return new(uri.AbsolutePath == "/api/studio/apply" ? ApplyStatus : HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
