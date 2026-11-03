using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class SpriteDisplayScaleTests
{
    [Theory]
    [InlineData(2560, 1600, 3)]
    [InlineData(1920, 1080, 1.818181818)]
    [InlineData(3840, 2160, 4.272727273)]
    [InlineData(3440, 1440, 2.636363636)]
    [InlineData(1280, 720, 1)]
    [InlineData(1366, 768, 1.109090909)]
    [InlineData(1024, 600, 1)]
    [InlineData(1280, 1600, 1)]
    [InlineData(7680, 4320, 9.181818182)]
    [InlineData(0, 0, 3)]
    public void ResolutionSelectsAContinuousScale(int width, int height, double expected) =>
        Assert.Equal(expected, SpriteDisplayScale.ForDisplay(new ScreenRect(-1920, 100, width, height)), precision: 8);

    [Fact]
    public void NearbyResolutionsChangeScaleWithoutIntegerSteps()
    {
        double before = SpriteDisplayScale.ForDisplay(new ScreenRect(0, 0, 1920, 1080));
        double after = SpriteDisplayScale.ForDisplay(new ScreenRect(0, 0, 1921, 1081));

        Assert.InRange(after - before, 0.001, 0.01);
    }
}
