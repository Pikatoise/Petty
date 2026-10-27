using Petty.Core.Domain;

namespace Petty.Tests;

public class BowlPlacementTests
{
    private static readonly DisplayInfo Primary = new(
        "DISPLAY1",
        true,
        new ScreenRect(0, 0, 1920, 1080),
        new ScreenRect(0, 0, 1920, 1040));

    private static readonly DisplayInfo Secondary = new(
        "DISPLAY2",
        false,
        new ScreenRect(-1280, 0, 1280, 1024),
        new ScreenRect(-1280, 0, 1280, 984));

    private static readonly DisplayInfo[] Displays = [Primary, Secondary];

    [Fact]
    public void RelativePositionIsResolvedOnTheSavedDisplay()
    {
        var resolved = BowlPlacement.Resolve(new BowlPlacementState(0.5, 100, "DISPLAY2"), Displays, 64, 64);

        Assert.Equal("DISPLAY2", resolved.DisplayDeviceName);
        Assert.Equal(-1280 + (0.5 * (1280 - 64)), resolved.Left, 3);
        Assert.Equal(984 - 64 - 100, resolved.Top, 3);
    }

    [Fact]
    public void MissingDisplayFallsBackToPrimary()
    {
        var resolved = BowlPlacement.Resolve(new BowlPlacementState(0.5, 100, "UNPLUGGED"), Displays, 64, 64);

        Assert.Equal("DISPLAY1", resolved.DisplayDeviceName);
        Assert.InRange(resolved.Left, Primary.WorkArea.X, Primary.WorkArea.Right - 64);
    }

    [Fact]
    public void OutOfRangeValuesAreClampedIntoTheWorkArea()
    {
        var resolved = BowlPlacement.Resolve(new BowlPlacementState(5, 5000, null), Displays, 64, 64);

        Assert.InRange(resolved.Left, Primary.WorkArea.X, Primary.WorkArea.Right - 64);
        Assert.True(resolved.Left + 64 <= Primary.WorkArea.Right);
        Assert.Equal(Primary.WorkArea.Y, resolved.Top, 3);
    }

    [Fact]
    public void ResolutionChangeKeepsTheBowlOnScreen()
    {
        var small = new DisplayInfo("DISPLAY1", true, new ScreenRect(0, 0, 800, 600), new ScreenRect(0, 0, 800, 560));
        var resolved = BowlPlacement.Resolve(new BowlPlacementState(0.99, 300, "DISPLAY1"), [small], 64, 64);

        Assert.True(resolved.Left + 64 <= small.WorkArea.Right + 0.001);
        Assert.True(resolved.Top >= small.WorkArea.Y);
        Assert.True(resolved.Top + 64 <= small.WorkArea.Bottom + 0.001);
    }

    [Fact]
    public void CaptureAndResolveRoundTrip()
    {
        var captured = BowlPlacement.Capture(400, 700, 64, 64, Displays);
        var resolved = BowlPlacement.Resolve(captured, Displays, 64, 64);

        Assert.Equal("DISPLAY1", captured.DisplayDeviceName);
        Assert.Equal(400, resolved.Left, 3);
        Assert.Equal(700, resolved.Top, 3);
    }

    [Fact]
    public void CaptureOnSecondaryMonitorUsesThatMonitor()
    {
        var captured = BowlPlacement.Capture(-1000, 500, 64, 64, Displays);
        var resolved = BowlPlacement.Resolve(captured, Displays, 64, 64);

        Assert.Equal("DISPLAY2", captured.DisplayDeviceName);
        Assert.Equal(-1000, resolved.Left, 3);
    }

    [Fact]
    public void NoDisplaysIsAnError()
    {
        Assert.Throws<ArgumentException>(() =>
            BowlPlacement.Resolve(new BowlPlacementState(0.5, 0, null), Array.Empty<DisplayInfo>(), 64, 64));
    }
}
