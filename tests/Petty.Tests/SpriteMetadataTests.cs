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
    public void ValidMetadataHasNoErrors() => Assert.Empty(SpriteMetadataValidator.Validate(Valid()));

    [Theory]
    [InlineData(64, 45)]
    [InlineData(58, 0)]
    public void PlayContactOutsideSpriteIsRejected(int x, int y)
    {
        SpriteSheetMetadata metadata = Valid();
        metadata.PlayContactX = x;
        metadata.PlayContactY = y;
        Assert.Contains(SpriteMetadataValidator.Validate(metadata), error => error.Contains("Play paw contact"));
    }

    [Fact]
    public void SpeciesIsOptionalForLegacyAndLimitedToCatsOrDogs()
    {
        SpriteSheetMetadata metadata = Valid();
        Assert.Empty(SpriteMetadataValidator.Validate(metadata));
        metadata.SpeciesId = "dog";
        Assert.Empty(SpriteMetadataValidator.Validate(metadata));
        metadata.SpeciesId = "other";
        Assert.Contains(SpriteMetadataValidator.Validate(metadata), error => error.Contains("Species"));
    }

    [Fact]
    public void MissingRequiredClipIsReported()
    {
        SpriteSheetMetadata metadata = Valid();
        metadata.Animations.RemoveAll(a => a.Name == "sleep");

        IReadOnlyList<string> errors = SpriteMetadataValidator.Validate(metadata);

        Assert.Contains(errors, e => e.Contains("sleep"));
    }

    [Fact]
    public void FramesOutsideTheGridAreReported()
    {
        SpriteSheetMetadata metadata = Valid();
        metadata.Animations[^1].StartIndex = 60;

        IReadOnlyList<string> errors = SpriteMetadataValidator.Validate(metadata);

        Assert.Contains(errors, e => e.Contains("past the atlas"));
    }

    [Fact]
    public void DuplicateClipNamesAndBadFrameRateAreReported()
    {
        SpriteSheetMetadata metadata = Valid();
        metadata.Animations.Add(new AnimationClipMetadata { Name = "IDLE", StartIndex = 50, FrameCount = 1, FramesPerSecond = 0 });

        IReadOnlyList<string> errors = SpriteMetadataValidator.Validate(metadata);

        Assert.Contains(errors, e => e.Contains("Duplicate"));
        Assert.Contains(errors, e => e.Contains("frame rate"));
    }

    [Fact]
    public void InvalidFrameSizeIsReported()
    {
        SpriteSheetMetadata metadata = Valid();
        metadata.FrameWidth = 2;

        Assert.Contains(SpriteMetadataValidator.Validate(metadata), e => e.Contains("FrameWidth"));
    }

    [Fact]
    public void CellPositionIsRowMajor()
    {
        SpriteSheetMetadata metadata = Valid();
        Assert.Equal((0, 0), SpriteMetadataValidator.CellPosition(metadata, 0));
        Assert.Equal((7, 0), SpriteMetadataValidator.CellPosition(metadata, 7));
        Assert.Equal((0, 1), SpriteMetadataValidator.CellPosition(metadata, 8));
    }

    [Fact]
    public void NullMetadataIsReported() => Assert.NotEmpty(SpriteMetadataValidator.Validate(null));

    [Theory]
    [InlineData("../other.png")]
    [InlineData("other.png")]
    public void RejectsMismatchedOrEscapingImageNames(string image)
    {
        SpriteSheetMetadata metadata = Valid();
        metadata.Image = image;
        Assert.NotEmpty(SpriteMetadataValidator.Validate(metadata));
    }

    [Fact]
    public void RejectsOverflowingFrameRanges()
    {
        SpriteSheetMetadata metadata = Valid();
        metadata.Animations[0].StartIndex = int.MaxValue;
        Assert.Contains(SpriteMetadataValidator.Validate(metadata), error => error.Contains("past the atlas"));
    }

    [Fact]
    public void ValidatesFramePixelsAndUnusedCells()
    {
        SpriteSheetMetadata metadata = Valid();
        int width = metadata.Columns * metadata.FrameWidth;
        byte[] pixels = new byte[width * metadata.Rows * metadata.FrameHeight * 4];
        foreach (AnimationClipMetadata clip in metadata.Animations)
        {
            for (int i = 0; i < clip.FrameCount; i++)
            {
                int cell = clip.StartIndex + i;
                int x = cell % metadata.Columns * 64 + 10;
                int y = cell / metadata.Columns * 64 + metadata.GroundRow;
                pixels[(y * width + x) * 4 + 3] = 255;
            }
        }

        Assert.Empty(SpritePixels.Validate(metadata, pixels));
        pixels[(metadata.GroundRow * width + 10) * 4 + 3] = 128;
        Assert.Contains(SpritePixels.Validate(metadata, pixels), e => e.Contains("semi-transparent"));
        pixels[(metadata.GroundRow * width + 10) * 4 + 3] = 255;
        pixels[(6 * 64 * width + 10) * 4 + 3] = 255;
        Assert.Contains(SpritePixels.Validate(metadata, pixels), e => e.Contains("Unused"));
    }
}
