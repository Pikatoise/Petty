using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class WalkSurfaceTests
{
    private const double Width = 192;

    private static DisplayInfo Display(ScreenRect bounds, ScreenRect? work = null) =>
        new("DISPLAY1", true, bounds, work ?? bounds);

    [Fact]
    public void HiddenTaskbarLetsThePetWalkTheBottomEdge()
    {
        var display = Display(new ScreenRect(0, 0, 2560, 1600));

        var area = WalkSurface.Compute(display, visibleBar: null, Width);

        Assert.Equal(0, area.Left);
        Assert.Equal(2560 - Width, area.Right);
        Assert.Equal(1600, area.FloorY);
    }

    [Fact]
    public void TheWholeScreenWidthIsWalkable()
    {
        var display = Display(new ScreenRect(0, 0, 2560, 1600));

        var area = WalkSurface.Compute(display, visibleBar: null, Width);

        // Window-left limits: 0 puts the pet at the left edge, 2368 puts its right
        // edge at 2560, so every column of the screen can be reached.
        Assert.Equal(0, area.Left);
        Assert.Equal(2368, area.Right);
    }

    [Fact]
    public void RevealedAutoHideTaskbarRaisesTheFloor()
    {
        // Auto-hide: Windows reports the full screen as work area, but the bar is
        // on screen right now, so the pet has to stand on top of it.
        var display = Display(new ScreenRect(0, 0, 2560, 1600));

        var area = WalkSurface.Compute(display, new ScreenRect(0, 1504, 2560, 96), Width);

        Assert.Equal(1504, area.FloorY);
        Assert.Equal(2560 - Width, area.Right);
    }

    [Fact]
    public void DockedTaskbarKeepsTheFloorAboveIt()
    {
        var display = Display(
            new ScreenRect(0, 0, 2560, 1600),
            new ScreenRect(0, 0, 2560, 1504));

        var area = WalkSurface.Compute(display, new ScreenRect(0, 1504, 2560, 96), Width);

        Assert.Equal(1504, area.FloorY);
    }

    [Fact]
    public void WorkAreaAloneIsEnoughWhenTheBarIsNotReported()
    {
        var display = Display(
            new ScreenRect(0, 0, 2560, 1600),
            new ScreenRect(0, 0, 2560, 1504));

        var area = WalkSurface.Compute(display, visibleBar: null, Width);

        Assert.Equal(1504, area.FloorY);
    }

    [Fact]
    public void RetractedSliverIsNotAnObstacle()
    {
        var display = Display(new ScreenRect(0, 0, 2560, 1600));

        var area = WalkSurface.Compute(display, new ScreenRect(0, 1598, 2560, 2), Width);

        Assert.Equal(1600, area.FloorY);
    }

    [Fact]
    public void TopTaskbarDoesNotMoveTheFloor()
    {
        var display = Display(
            new ScreenRect(0, 0, 2560, 1600),
            new ScreenRect(0, 48, 2560, 1552));

        var area = WalkSurface.Compute(display, new ScreenRect(0, 0, 2560, 48), Width);

        Assert.Equal(1600, area.FloorY);
        Assert.Equal(0, area.Left);
    }

    [Fact]
    public void SideTaskbarsNarrowTheWalkRange()
    {
        var display = Display(new ScreenRect(0, 0, 2560, 1600));

        var leftBar = WalkSurface.Compute(display, new ScreenRect(0, 0, 96, 1600), Width);
        Assert.Equal(96, leftBar.Left);
        Assert.Equal(2560 - Width, leftBar.Right);

        var rightBar = WalkSurface.Compute(display, new ScreenRect(2464, 0, 96, 1600), Width);
        Assert.Equal(2464 - Width, rightBar.Right);
        Assert.Equal(0, rightBar.Left);
    }

    [Fact]
    public void SecondMonitorOffsetsAreHonoured()
    {
        var display = Display(new ScreenRect(-1920, -200, 1920, 1080));

        var area = WalkSurface.Compute(display, new ScreenRect(-1920, 680, 1920, 200), Width);

        Assert.Equal(-1920, area.Left);
        Assert.Equal(680, area.FloorY);
        Assert.Equal(-1920 + 1920 - Width, area.Right);
    }

    [Fact]
    public void BarOnAnotherMonitorIsIgnored()
    {
        var display = Display(new ScreenRect(0, 0, 2560, 1600));

        // A bar rectangle that does not intersect this monitor must not shrink it.
        var area = WalkSurface.Compute(display, new ScreenRect(-1920, 1504, 1920, 96), Width);

        Assert.Equal(1600, area.FloorY);
        Assert.Equal(0, area.Left);
    }

    [Fact]
    public void ScreenNarrowerThanThePetCollapsesToASingleColumn()
    {
        var display = Display(new ScreenRect(0, 0, 100, 800));

        var area = WalkSurface.Compute(display, visibleBar: null, Width);

        Assert.Equal(0, area.Left);
        Assert.Equal(0, area.Right);
    }

    [Fact]
    public void EmptyDisplayProducesAnEmptyArea()
    {
        var display = Display(new ScreenRect(0, 0, 0, 0));

        var area = WalkSurface.Compute(display, visibleBar: null, Width);

        Assert.False(area.HasRoom);
    }

    [Fact]
    public void EdgeClassificationMatchesTheDockedEdge()
    {
        var monitor = new ScreenRect(0, 0, 2560, 1600);

        Assert.Equal(ShellBarEdge.Bottom, WalkSurface.EdgeOf(monitor, new ScreenRect(0, 1504, 2560, 96)));
        Assert.Equal(ShellBarEdge.Top, WalkSurface.EdgeOf(monitor, new ScreenRect(0, 0, 2560, 48)));
        Assert.Equal(ShellBarEdge.Left, WalkSurface.EdgeOf(monitor, new ScreenRect(0, 0, 96, 1600)));
        Assert.Equal(ShellBarEdge.Right, WalkSurface.EdgeOf(monitor, new ScreenRect(2464, 0, 96, 1600)));
        Assert.Equal(ShellBarEdge.None, WalkSurface.EdgeOf(monitor, new ScreenRect(900, 700, 300, 200)));
    }
}
