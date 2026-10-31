using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class InteractionMenuPlacementTests
{
    [Fact]
    public void HoverUsesEntireExposedWindowButNotCoveredOrOutside()
    {
        var pet = new ScreenRect(100, 200, 192, 192);
        Assert.True(InteractionMenuPlacement.IsPetHovered(pet, 101, 201, true));
        Assert.False(InteractionMenuPlacement.IsPetHovered(pet, 101, 201, false));
        Assert.False(InteractionMenuPlacement.IsPetHovered(pet, 292, 201, true));
    }

    [Theory]
    [InlineData(500, 600)]
    [InlineData(0, 600)]
    [InlineData(1000, 600)]
    [InlineData(500, 0)]
    [InlineData(0, 0)]
    [InlineData(1000, 0)]
    public void MenuStaysVisibleAndTouchesAnchor(double x, double y)
    {
        var area = new ScreenRect(0, 0, 1000, 700);
        var menu = InteractionMenuPlacement.Place(area, x, y, 220, 60);
        Assert.InRange(menu.X, area.X, area.Right - menu.Width);
        Assert.InRange(menu.Y, area.Y, area.Bottom - menu.Height);
        Assert.InRange(x, menu.X - 1, menu.Right + 1);
        Assert.InRange(y, menu.Y - 1, menu.Bottom + 1);
    }
}
