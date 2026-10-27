using Petty.Core.Runtime;

namespace Petty.Tests;

public class PetMotionTests
{
    private static MotionArea Area => new(Left: 0, Right: 1000, FloorY: 800);

    [Fact]
    public void EnterStartsOffScreenLeftAndFacesRight()
    {
        var motion = new PetMotion();
        motion.Enter(Area, windowWidth: 192);

        Assert.Equal(-192, motion.X);
        Assert.Equal(1, motion.Facing);
        Assert.True(motion.IsMoving);
    }

    [Fact]
    public void EnterStopsInsideTheArea()
    {
        var motion = new PetMotion();
        motion.Enter(Area, windowWidth: 192);

        for (var i = 0; i < 400; i++) motion.Update(TimeSpan.FromMilliseconds(50));

        Assert.False(motion.IsMoving);
        Assert.InRange(motion.X, 0, 1000);
        Assert.True(motion.X > 100, "the cat should have walked onto the desktop");
    }

    [Fact]
    public void WanderTargetsStayInsideTheArea()
    {
        var motion = new PetMotion(x: 500, random: new Random(7));
        motion.Wander(Area, run: true);

        Assert.True(motion.HasTarget);
        Assert.InRange(motion.TargetX, Area.Left, Area.Right);
        Assert.Equal(PetMotion.RunSpeed, motion.Speed);
    }

    [Fact]
    public void FacingFollowsTheDirectionOfTravel()
    {
        var motion = new PetMotion(x: 900);

        motion.MoveTo(100, 50);
        Assert.Equal(-1, motion.Facing);

        motion.Place(100);
        motion.MoveTo(500, 50);
        Assert.Equal(1, motion.Facing);
    }

    [Fact]
    public void WanderAlwaysProducesAReachableTarget()
    {
        for (var seed = 0; seed < 20; seed++)
        {
            var motion = new PetMotion(x: 500, random: new Random(seed));
            motion.Wander(new MotionArea(0, 920, 800), run: false);

            Assert.InRange(motion.TargetX, 0, 920);
        }
    }

    [Fact]
    public void WanderSometimesCrossesTheWholeScreen()
    {
        var area = new MotionArea(0, 2000, 800);
        var edgeWalks = 0;

        for (var seed = 0; seed < 40; seed++)
        {
            var motion = new PetMotion(x: 100, random: new Random(seed));
            motion.Wander(area, run: true);

            if (motion.TargetX == area.Left || motion.TargetX == area.Right) edgeWalks++;
        }

        Assert.True(edgeWalks >= 5, $"expected several walks to a screen edge, got {edgeWalks} of 40");
    }

    [Fact]
    public void MovementStopsExactlyOnTarget()
    {
        var motion = new PetMotion(x: 100);
        motion.MoveTo(300, 100);

        motion.Update(TimeSpan.FromSeconds(1));
        Assert.Equal(200, motion.X, precision: 3);

        motion.Update(TimeSpan.FromSeconds(5));
        Assert.Equal(300, motion.X, precision: 3);
        Assert.False(motion.IsMoving);
    }

    [Fact]
    public void NonPositiveDeltaDoesNotMove()
    {
        var motion = new PetMotion(x: 100);
        motion.MoveTo(400, 100);

        Assert.False(motion.Update(TimeSpan.Zero));
        Assert.False(motion.Update(TimeSpan.FromMilliseconds(-20)));
        Assert.Equal(100, motion.X);
    }

    [Fact]
    public void ExitRightLeavesTheAreaCompletely()
    {
        var motion = new PetMotion(x: 700);
        motion.Exit(Area, windowWidth: 192, direction: 1);

        Assert.Equal(1, motion.Facing);
        for (var i = 0; i < 100; i++) motion.Update(TimeSpan.FromMilliseconds(100));

        Assert.True(motion.X >= Area.Right + 192, $"expected the window to clear the edge, got {motion.X}");
    }

    [Fact]
    public void ExitWithoutDirectionUsesTheNearestEdge()
    {
        var left = new PetMotion(x: 100);
        left.Exit(Area, 192);
        Assert.Equal(-1, left.Facing);

        var right = new PetMotion(x: 900);
        right.Exit(Area, 192);
        Assert.Equal(1, right.Facing);
    }

    [Fact]
    public void ExitLeftLeavesTheAreaCompletely()
    {
        var motion = new PetMotion(x: 100);
        motion.Exit(Area, windowWidth: 192, direction: -1);

        for (var i = 0; i < 100; i++) motion.Update(TimeSpan.FromMilliseconds(100));

        Assert.True(motion.X <= Area.Left - 192);
    }

    [Fact]
    public void EmptyAreaCannotWander()
    {
        var motion = new PetMotion(x: 10);
        motion.Wander(new MotionArea(10, 10, 800), run: false);

        Assert.False(motion.IsMoving);
    }

    [Fact]
    public void KeepInsideClampsOnlyWhenIdle()
    {
        var motion = new PetMotion(x: 5000);
        motion.KeepInside(new MotionArea(0, 800, 800));
        Assert.Equal(800, motion.X);

        motion.MoveTo(1200, 50);
        motion.KeepInside(new MotionArea(0, 300, 800));
        Assert.Equal(800, motion.X); // an active walk is not teleported
    }

    [Fact]
    public void PlaceStopsMovement()
    {
        var motion = new PetMotion(x: 10);
        motion.MoveTo(500, 100);
        motion.Place(42);

        Assert.Equal(42, motion.X);
        Assert.False(motion.IsMoving);
    }
}
