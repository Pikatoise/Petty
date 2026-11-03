using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class BowlDropTests
{
    [Fact]
    public void ReleaseFallsSmoothlyAndLandsAtTheConfiguredTime()
    {
        var drop = new BowlDrop();
        TimeSpan duration = PetTuning.Default.BowlDropDuration;
        drop.Start(-600);

        Assert.Equal(-600, drop.Advance(TimeSpan.Zero, duration));
        double first = drop.Advance(duration / 4, duration);
        double middle = drop.Advance(duration / 4, duration);
        double third = drop.Advance(duration / 4, duration);
        Assert.InRange(first, -600, -500);
        Assert.Equal(-300, middle, 6);
        Assert.InRange(third, -100, 0);
        Assert.True(drop.IsMoving);
        Assert.Equal(0, drop.Advance(duration / 4, duration));
        Assert.False(drop.IsMoving);
    }

    [Fact]
    public void RedragRestartsTheFallAndRepositionCancelsIt()
    {
        var drop = new BowlDrop();
        drop.Start(-600);
        drop.Advance(TimeSpan.FromSeconds(0.2), PetTuning.Default.BowlDropDuration);
        drop.Start(-200);
        Assert.Equal(-200, drop.Advance(TimeSpan.Zero, PetTuning.Default.BowlDropDuration));
        drop.Stop();
        Assert.False(drop.IsMoving);
        Assert.Equal(0, drop.Advance(TimeSpan.FromSeconds(2), PetTuning.Default.BowlDropDuration));
    }
}
