using Petty.Core.Domain;

namespace Petty.Tests;

public class BowlTests
{
    [Fact]
    public void FillRespectsCapacityAndReturnsAddedAmount()
    {
        var bowl = new Bowl(0, 100);

        Assert.Equal(25, bowl.Fill(25));
        Assert.Equal(25, bowl.Food);
        Assert.Equal(75, bowl.Fill(1000));
        Assert.Equal(100, bowl.Food);
        Assert.Equal(0, bowl.Fill(10));
    }

    [Fact]
    public void ConsumeNeverGoesNegative()
    {
        var bowl = new Bowl(10, 100);

        Assert.Equal(10, bowl.Consume(50));
        Assert.Equal(0, bowl.Food);
        Assert.False(bowl.HasFood);
        Assert.Equal(0, bowl.Consume(5));
    }

    [Fact]
    public void InvalidAmountsAreIgnored()
    {
        var bowl = new Bowl(20, 100);

        Assert.Equal(0, bowl.Fill(-5));
        Assert.Equal(0, bowl.Fill(double.NaN));
        Assert.Equal(0, bowl.Consume(-1));
        Assert.Equal(20, bowl.Food);
    }

    [Fact]
    public void ConstructorClampsInitialFood()
    {
        Assert.Equal(100, new Bowl(500, 100).Food);
        Assert.Equal(0, new Bowl(-5, 100).Food);
    }
}
