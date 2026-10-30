using Petty.Core.Assets;

namespace Petty.Tests;

public class SpriteMetadataTests
{
    private static SpriteSheetMetadata Valid() => new()
    {
        Character = "cat-orange-white",
        Image = "cat-orange-white.png",
        FrameWidth = 64,
        FrameHeight = 64,
        Columns = 8,
        Rows = 7,
        Animations = new List<AnimationClipMetadata>
        {
            new() { Name = "idle", StartIndex = 0, FrameCount = 4, FramesPerSecond = 6 },
            new() { Name = "walk", StartIndex = 4, FrameCount = 8, FramesPerSecond = 10 },
            new() { Name = "run", StartIndex = 12, FrameCount = 8, FramesPerSecond = 14 },
            new() { Name = "eat", StartIndex = 20, FrameCount = 6, FramesPerSecond = 8 },
            new() { Name = "sleep", StartIndex = 26, FrameCount = 4, FramesPerSecond = 4 },
            new() { Name = "play", StartIndex = 30, FrameCount = 8, FramesPerSecond = 12 },
            new() { Name = "tired", StartIndex = 38, FrameCount = 4, FramesPerSecond = 5 },
            new() { Name = "exit", StartIndex = 42, FrameCount = 6, FramesPerSecond = 10, Loop = false },
        },
    };

    [Fact]
    public void ValidMetadataHasNoErrors()
    {
        Assert.Empty(SpriteMetadataValidator.Validate(Valid()));
    }

    [Fact]
    public void MissingRequiredClipIsReported()
    {
        var metadata = Valid();
        metadata.Animations.RemoveAll(a => a.Name == "sleep");

        var errors = SpriteMetadataValidator.Validate(metadata);

        Assert.Contains(errors, e => e.Contains("sleep"));
    }

    [Fact]
    public void FramesOutsideTheGridAreReported()
    {
        var metadata = Valid();
        metadata.Animations[^1].StartIndex = 60;

        var errors = SpriteMetadataValidator.Validate(metadata);

        Assert.Contains(errors, e => e.Contains("past the atlas"));
    }

    [Fact]
    public void DuplicateClipNamesAndBadFrameRateAreReported()
    {
        var metadata = Valid();
        metadata.Animations.Add(new AnimationClipMetadata { Name = "IDLE", StartIndex = 50, FrameCount = 1, FramesPerSecond = 0 });

        var errors = SpriteMetadataValidator.Validate(metadata);

        Assert.Contains(errors, e => e.Contains("Duplicate"));
        Assert.Contains(errors, e => e.Contains("frame rate"));
    }

    [Fact]
    public void InvalidFrameSizeIsReported()
    {
        var metadata = Valid();
        metadata.FrameWidth = 2;

        Assert.Contains(SpriteMetadataValidator.Validate(metadata), e => e.Contains("FrameWidth"));
    }

    [Fact]
    public void CellPositionIsRowMajor()
    {
        var metadata = Valid();
        Assert.Equal((0, 0), SpriteMetadataValidator.CellPosition(metadata, 0));
        Assert.Equal((7, 0), SpriteMetadataValidator.CellPosition(metadata, 7));
        Assert.Equal((0, 1), SpriteMetadataValidator.CellPosition(metadata, 8));
    }

    [Fact]
    public void NullMetadataIsReported()
    {
        Assert.NotEmpty(SpriteMetadataValidator.Validate(null));
    }

    [Theory]
    [InlineData("../other.png")]
    [InlineData("other.png")]
    public void RejectsMismatchedOrEscapingImageNames(string image)
    {
        var metadata = Valid();
        metadata.Image = image;
        Assert.NotEmpty(SpriteMetadataValidator.Validate(metadata));
    }

    [Fact]
    public void RejectsOverflowingFrameRanges()
    {
        var metadata = Valid();
        metadata.Animations[0].StartIndex = int.MaxValue;
        Assert.Contains(SpriteMetadataValidator.Validate(metadata), error => error.Contains("past the atlas"));
    }

    [Fact]
    public void ValidatesFramePixelsAndUnusedCells()
    {
        var metadata = Valid();
        var width = metadata.Columns * metadata.FrameWidth;
        var pixels = new byte[width * metadata.Rows * metadata.FrameHeight * 4];
        foreach (var clip in metadata.Animations)
            for (var i = 0; i < clip.FrameCount; i++)
            {
                var cell = clip.StartIndex + i;
                var x = cell % metadata.Columns * 64 + 10;
                var y = cell / metadata.Columns * 64 + metadata.GroundRow;
                pixels[(y * width + x) * 4 + 3] = 255;
            }
        Assert.Empty(SpritePixels.Validate(metadata, pixels));
        pixels[(metadata.GroundRow * width + 10) * 4 + 3] = 128;
        Assert.Contains(SpritePixels.Validate(metadata, pixels), e => e.Contains("semi-transparent"));
        pixels[(metadata.GroundRow * width + 10) * 4 + 3] = 255;
        pixels[(6 * 64 * width + 10) * 4 + 3] = 255;
        Assert.Contains(SpritePixels.Validate(metadata, pixels), e => e.Contains("Unused"));
    }
}
