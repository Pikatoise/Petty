using Petty.Core.Domain;
using Petty.Core.Runtime;

namespace Petty.Tests;

public class BowlPresentationTests
{
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 1)] [InlineData(25, 1)] [InlineData(26, 2)]
    [InlineData(50, 2)] [InlineData(51, 3)] [InlineData(75, 3)] [InlineData(76, 4)] [InlineData(100, 4)]
    public void FoodRoundsUpToVisibleQuarters(double food, int expected) =>
        Assert.Equal(expected, BowlPresentation.Level(food, 100));

    [Fact]
    public void RepeatedClicksAreRejectedAndFourServingsFillTheBowl()
    {
        var bowl = new Bowl(0, 100);
        var display = new BowlPresentation();
        for (var i = 1; i <= 4; i++)
        {
            Assert.True(display.TryStart(bowl.Food, bowl.Capacity));
            Assert.Equal(25, bowl.Fill(25));
            Assert.False(display.TryStart(bowl.Food, bowl.Capacity));
            Assert.Equal(i, BowlPresentation.Level(bowl.Food, bowl.Capacity));
            display.Advance(BowlPresentation.PourDuration);
        }
        Assert.False(display.TryStart(bowl.Food, bowl.Capacity));
        bowl.Consume(80);
        Assert.Equal(1, BowlPresentation.Level(bowl.Food, bowl.Capacity));
        bowl.Consume(20);
        Assert.Equal(0, BowlPresentation.Level(bowl.Food, bowl.Capacity));
    }
}
