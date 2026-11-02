using Petty.Core.Domain;

namespace Petty.Tests;

public class PetTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("This name is definitely far too long for a cat")]
    public void InvalidNamesAreRejected(string input)
    {
        Assert.False(PetNameRules.TryNormalize(input, out _, out string? error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ValidNamesAreTrimmed()
    {
        Assert.True(PetNameRules.TryNormalize("  Muffin  ", out string? name, out _));
        Assert.Equal("Muffin", name);
    }
}
