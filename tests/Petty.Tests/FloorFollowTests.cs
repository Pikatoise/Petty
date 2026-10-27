using Petty.Core.Runtime;

namespace Petty.Tests;

public class FloorFollowTests
{
    [Fact]
    public void StartsOnTheGivenFloor()
    {
        var follow = new FloorFollow(1600);

        Assert.Equal(1600, follow.Current);
        Assert.True(follow.IsAt(1600));
    }

    [Fact]
    public void RisesTowardsTheTargetAtTheConfiguredSpeed()
    {
        var follow = new FloorFollow(1600);

        // 900 px/s for one second would overshoot a 96 px climb, so it snaps exactly.
        follow.Advance(1504, TimeSpan.FromSeconds(1));
        Assert.Equal(1504, follow.Current);

        follow.Snap(1600);
        follow.Advance(1504, TimeSpan.FromMilliseconds(50));
        Assert.Equal(1600 - 45, follow.Current, precision: 3);
    }

    [Fact]
    public void NeverOvershootsTheTarget()
    {
        var follow = new FloorFollow(1600);

        for (var i = 0; i < 50; i++) follow.Advance(1504, TimeSpan.FromMilliseconds(16));

        Assert.Equal(1504, follow.Current);
        Assert.True(follow.IsAt(1504));
    }

    [Fact]
    public void FallsBackDownWhenTheTaskbarHides()
    {
        var follow = new FloorFollow(1504);

        for (var i = 0; i < 50; i++) follow.Advance(1600, TimeSpan.FromMilliseconds(16));

        Assert.Equal(1600, follow.Current);
    }

    [Fact]
    public void IgnoresInvalidTargetsAndTimeSteps()
    {
        var follow = new FloorFollow(1600);

        Assert.Equal(1600, follow.Advance(double.NaN, TimeSpan.FromSeconds(1)));
        Assert.Equal(1600, follow.Advance(double.PositiveInfinity, TimeSpan.FromSeconds(1)));
        Assert.Equal(1600, follow.Advance(1504, TimeSpan.Zero));
        Assert.Equal(1600, follow.Advance(1504, TimeSpan.FromMilliseconds(-50)));
    }

    [Fact]
    public void SnapIgnoresInvalidValues()
    {
        var follow = new FloorFollow(1600);

        follow.Snap(double.NaN);

        Assert.Equal(1600, follow.Current);
    }
}
