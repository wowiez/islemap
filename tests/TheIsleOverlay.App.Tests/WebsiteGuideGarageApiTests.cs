using System.Net.Http;
using TheIsleOverlay.IslePilot;

namespace TheIsleOverlay.App.Tests;

public sealed class WebsiteGuideGarageApiTests
{
    [Fact]
    public void Sdvn3_ShowsItsGarageAndKeepsTheSbtcOnlyStudioToolsHidden()
    {
        using var http = new HttpClient();

        var api = WebsiteGuideGarageApi.Create(http, TelemetrySourceDefinition.Sdvn3, "synthetic-cookie");

        Assert.True(api.ShowGarage);
        Assert.False(api.StudioFeatures);
        Assert.Equal("SDVN #3", api.Label);
        Assert.Equal("https://3.sdvn.org/skin", api.StudioUri.AbsoluteUri);
    }

    [Fact]
    public void SbtcGarage_KeepsItsStudioToolsAndHiddenVaultPage()
    {
        var api = SbtcGuideGarageApi.Create(new HttpClient(), TelemetrySourceDefinition.SbtcIsland, "session=synthetic");

        Assert.False(api.ShowGarage);
        Assert.True(api.StudioFeatures);
        Assert.Equal("https://sbtcislandd.com/studio", api.StudioUri.AbsoluteUri);
    }

    [Theory]
    [InlineData("glitch", true)]
    [InlineData("glitched", true)]
    [InlineData("standard", false)]
    public void GlitchDrafts_FromBothSitesOpenTheWebStudio(string renderMode, bool glitch)
    {
        var draft = new SkinDraftPresentation("Skin", new IslePilotOverlayGaragePaletteDto { Body = "#FFFFFF" }, ["#FFFFFF"],
            new IslePilotOverlaySkinDraftPayloadDto { RenderMode = renderMode });

        Assert.Equal(glitch, draft.IsGlitch);
        Assert.Equal(glitch ? "MỞ STUDIO" : "CHỌN", draft.ActionLabel);
        // SDVN sends glitch designs straight to the game.
        Assert.Equal(glitch ? "ÁP DỤNG" : "CHỌN", (draft with { SendsGlitch = true }).ActionLabel);
    }

    [Theory]
    [InlineData("https://isle-sdvn.local/cdn/skinviewer/Dibble/Diabloceratops.glb", "/cdn/skinviewer/Dibble/Diabloceratops.glb")]
    [InlineData("https://isle-sdvn.local/cdn/skinviewer/shared/T_SkinNoise_M.png", "/cdn/skinviewer/shared/T_SkinNoise_M.png")]
    [InlineData("https://isle-sdvn.local/api/player/garage", null)]
    [InlineData("https://isle-sdvn.local/cdn/skinviewer/../../garage", null)]
    [InlineData("https://isle-sdvn.local/cdn/skinviewer/a.png?x=1", null)]
    [InlineData("https://evil.example/cdn/skinviewer/a.png", null)]
    [InlineData("http://isle-sdvn.local/cdn/skinviewer/a.png", null)]
    public void StudioAssetProxy_OnlyServesThePublicSkinViewerTree(string request, string? expected) =>
        Assert.Equal(expected, GarageModelControl.StudioAssetPath(request));

    [Fact]
    public void Sdvn3_SendsGlitchDraftsDirectly()
    {
        using var http = new HttpClient();
        Assert.NotNull(WebsiteGuideGarageApi.Create(http, TelemetrySourceDefinition.Sdvn3, "synthetic-cookie").ApplyDraft);
        Assert.Null(SbtcGuideGarageApi.Create(http, TelemetrySourceDefinition.SbtcIsland, "session=synthetic").ApplyDraft);
    }
}
