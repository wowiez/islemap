using TheIsleOverlay.App;

namespace TheIsleOverlay.App.Tests;

public class DrinkingWaterOverlayTests
{
    [Fact]
    public void Mask_RemovesOpaqueAndTranslucentLand_PreservesWaterAndTransparentHoles()
    {
        byte[] pixels = [0, 0, 0, 255, 30, 40, 35, 90, 240, 220, 0, 255, 240, 220, 0, 0];
        DrinkingWaterOverlay.IsolateWater(pixels);
        Assert.Equal(0, pixels[3]);
        Assert.Equal(0, pixels[7]);
        Assert.Equal(255, pixels[11]);
        Assert.Equal(0, pixels[15]);
    }
}
