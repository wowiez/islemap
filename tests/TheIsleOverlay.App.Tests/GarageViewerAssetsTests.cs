using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Http;

namespace TheIsleOverlay.App.Tests;

public sealed class GarageViewerAssetsTests
{
    [Fact]
    public void Viewer_UsesBundledForestWaterfallBackground()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestAssets", "GarageViewer");
        var html = File.ReadAllText(Path.Combine(root, "viewer.html"));
        var imagePath = Path.Combine(root, "forest-waterfall.png");

        Assert.Contains("url('./forest-waterfall.png')", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http", html[html.IndexOf("body{background", StringComparison.Ordinal)..
            html.IndexOf("}canvas", StringComparison.Ordinal)], StringComparison.OrdinalIgnoreCase);

        using var stream = File.OpenRead(imagePath);
        Span<byte> header = stackalloc byte[24];
        Assert.Equal(header.Length, stream.Read(header));
        Assert.True(header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        var width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        Assert.True(width >= 1400);
        Assert.InRange((double)width / height, 1.7, 1.9);
        Assert.True(stream.Length > 1_000_000);
    }

    [Fact]
    public void ModelCache_RequiresCompleteGlbV2File()
    {
        var path = Path.Combine(Path.GetTempPath(), $"isle-model-{Guid.NewGuid():N}.glb");
        try
        {
            var valid = new byte[20];
            "glTF"u8.CopyTo(valid);
            BinaryPrimitives.WriteUInt32LittleEndian(valid.AsSpan(4, 4), 2);
            BinaryPrimitives.WriteUInt32LittleEndian(valid.AsSpan(8, 4), (uint)valid.Length);
            File.WriteAllBytes(path, valid);
            Assert.True(GarageModelControl.IsValidGlb(path));

            BinaryPrimitives.WriteUInt32LittleEndian(valid.AsSpan(8, 4), (uint)valid.Length + 4);
            File.WriteAllBytes(path, valid);
            Assert.False(GarageModelControl.IsValidGlb(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ViewerPolicy_AllowsMeshoptWasmWithoutAllowingJavaScriptEval()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestAssets", "GarageViewer");
        var html = File.ReadAllText(Path.Combine(root, "viewer.html"));
        Assert.Contains("'wasm-unsafe-eval'", html, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", html, StringComparison.Ordinal);
        Assert.Contains("https://isle-model.local", html, StringComparison.Ordinal);
    }

    [Fact]
    public void TextureCache_AcceptsPngAndWebpButRejectsHtml()
    {
        var path = Path.Combine(Path.GetTempPath(), $"isle-texture-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(path, [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 0, 0, 0, 0]);
            Assert.True(GarageModelControl.IsValidImage(path));

            File.WriteAllBytes(path, "RIFF1234WEBPdata"u8.ToArray());
            Assert.True(GarageModelControl.IsValidImage(path));

            File.WriteAllText(path, "<html>Cloudflare error</html>");
            Assert.False(GarageModelControl.IsValidImage(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Viewer_RecolorsOfficialPatternMasksInsteadOfTintingTheWholeMesh()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "TestAssets", "GarageViewer");
        var script = File.ReadAllText(Path.Combine(root, "viewer.js"));

        Assert.Contains("createSkinMaterial(diffuse, pattern, utility, data.utilityChannels)", script, StringComparison.Ordinal);
        Assert.Contains("setMeshoptDecoder(MeshoptDecoder)", script, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(root, "skin-material.js")));
        Assert.True(File.Exists(Path.Combine(root, "vendor", "meshopt_decoder.module.js")));
        Assert.DoesNotContain(": 'body';", script, StringComparison.Ordinal);
        Assert.Contains("targetRotationY - model.rotation.y", script, StringComparison.Ordinal);
        Assert.Contains("targetRotationZ - model.rotation.z", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextureDownload_FallsBackToPngWhenWebpIsMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), "isle-skin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var requests = new List<string>();
        try
        {
            var path = await GarageModelControl.DownloadImageAsync("https://sbtcislandd.com/assets/dino/Pteranodon/",
                "pattern_2", Path.Combine(dir, "pattern"), (uri, _) =>
                {
                    requests.Add(uri.AbsolutePath);
                    return uri.AbsolutePath.EndsWith(".webp")
                        ? Task.FromException<byte[]>(new HttpRequestException("Missing test WebP", null, HttpStatusCode.NotFound))
                        : Task.FromResult(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0, 0, 0, 0, 0 });
                });
            Assert.EndsWith("pattern_2.png", path);
            Assert.True(GarageModelControl.IsValidImage(path));
            Assert.Equal(new[] { "/assets/dino/Pteranodon/pattern_2.webp", "/assets/dino/Pteranodon/pattern_2.png" }, requests);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public async Task TextureDownload_UsesContractAssetAndDoesNotRetryAnExpiredSession()
    {
        var dir = Path.Combine(Path.GetTempPath(), "isle-skin-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var requests = new List<string>();
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => GarageModelControl.DownloadImageAsync(
                "https://sbtcislandd.com/assets/dino/Pteranodon/", "pattern_2", Path.Combine(dir, "pattern"),
                (uri, _) =>
                {
                    requests.Add(uri.AbsolutePath);
                    return Task.FromException<byte[]>(new HttpRequestException("Expired synthetic session", null, HttpStatusCode.Unauthorized));
                }, "pattern_contract.png"));
            Assert.Equal("/assets/dino/Pteranodon/pattern_contract.png", Assert.Single(requests));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
