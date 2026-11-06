using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public sealed class VisitorSceneTests
{
    private static readonly ScreenRect Screen = new(-1920, 0, 1920, 1080);
    private static readonly MotionArea Area = new(-1920, -192, 1040);
    private static VisitorScene Create(IReadOnlyList<ScreenRect>? obstacles = null) =>
        new(Area, Screen, 192, obstacles ?? [], TimeSpan.FromSeconds(0.8), TimeSpan.FromSeconds(0.4));

    [Fact]
    public void GuestArrivesSitsAndWaitsWithoutTimeoutThenLeavesOnlyAfterPhysicalExit()
    {
        VisitorScene guest = Create();
        Assert.Equal(Screen.X - 192, guest.X);
        Assert.Equal("mail-walk", guest.Clip);
        guest.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(VisitorPhase.Sitting, guest.Phase);
        Assert.Equal("mail-sit", guest.Clip);
        guest.Advance(TimeSpan.FromSeconds(0.8));
        Assert.Equal(VisitorPhase.Waiting, guest.Phase);
        double waitingX = guest.X;
        guest.Advance(TimeSpan.FromDays(1));
        Assert.Equal(VisitorPhase.Waiting, guest.Phase);
        Assert.Equal(waitingX, guest.X);
        guest.RequestExit();
        Assert.Equal("mail-exit", guest.Clip);
        guest.Advance(TimeSpan.FromSeconds(0.3));
        Assert.Equal(waitingX, guest.X);
        guest.RequestExit(); // Repeated refreshes must not restart the rise.
        guest.Advance(TimeSpan.FromSeconds(0.2));
        Assert.True(guest.X < waitingX);
        Assert.Equal("mail-walk", guest.Clip);
        Assert.Equal(VisitorPhase.Exiting, guest.Phase);
        guest.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(Screen.X - 192, guest.X);
        Assert.Equal(VisitorPhase.Finished, guest.Phase);
    }

    [Fact]
    public void BowlReservesItsEdgeAndRevocationCanReverseAnEnteringGuest()
    {
        VisitorScene guest = Create([new ScreenRect(-1900, 990, 120, 50)]);
        Assert.Equal(Screen.Right, guest.X);
        Assert.True(guest.FaceLeft);
        guest.Advance(TimeSpan.FromSeconds(0.5));
        Assert.True(guest.X < Screen.Right);
        guest.RequestExit();
        Assert.False(guest.FaceLeft);
        guest.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(VisitorPhase.Finished, guest.Phase);
        Assert.Equal(Screen.Right, guest.X);
    }

    [Fact]
    public void GeometryChangesRetargetExitAndRelocateAWaitingGuest()
    {
        VisitorScene guest = Create();
        guest.Advance(TimeSpan.FromSeconds(30));
        guest.Advance(TimeSpan.FromSeconds(1));
        var screen = new ScreenRect(0, 0, 1280, 720);
        var area = new MotionArea(0, 1152, 680);
        guest.UpdateSurface(area, screen, 128, []);
        Assert.InRange(guest.X, area.Left, area.Right);
        Assert.Equal(VisitorPhase.Entering, guest.Phase);
        guest.Advance(TimeSpan.FromSeconds(30));
        guest.Advance(TimeSpan.FromSeconds(1));
        guest.RequestExit();
        guest.Advance(TimeSpan.FromSeconds(0.5));
        screen = new ScreenRect(0, 0, 1920, 1080);
        guest.UpdateSurface(new MotionArea(0, 1792, 1040), screen, 128, []);
        guest.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(VisitorPhase.Finished, guest.Phase);
        Assert.Equal(-128, guest.X);
    }

    [Fact]
    public void RescalingDoesNotPullAPartiallyVisibleGuestBackInside()
    {
        VisitorScene guest = Create();
        guest.Advance(TimeSpan.FromSeconds(0.5));
        Assert.True(guest.X < Area.Left);
        MotionArea smaller = Area with
        {
            Right = -128
        };
        guest.UpdateSurface(smaller, Screen, 128, []);
        Assert.True(guest.X < smaller.Left);
        guest.RequestExit();
        guest.Advance(TimeSpan.FromSeconds(0.05));
        guest.UpdateSurface(Area, Screen, 192, []);
        Assert.True(guest.X < Area.Left);
        guest.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(VisitorPhase.Finished, guest.Phase);
    }

    [Fact]
    public void RelocatedVisitorLeavesThroughTheNearestEdge()
    {
        VisitorScene guest = Create();
        guest.Advance(TimeSpan.FromSeconds(30));
        guest.Advance(TimeSpan.FromSeconds(1));
        guest.UpdateSurface(Area, Screen, 192, [new ScreenRect(Area.Left, 990, 200, 50)]);
        guest.Advance(TimeSpan.FromMinutes(2));
        guest.Advance(TimeSpan.FromSeconds(1));
        Assert.True(guest.X > (Area.Left + Area.Right) / 2);
        guest.RequestExit();
        guest.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(VisitorPhase.Finished, guest.Phase);
        Assert.Equal(Screen.Right, guest.X);
    }
}
