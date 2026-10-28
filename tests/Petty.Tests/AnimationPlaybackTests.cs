using Petty.Core.Animation;
using Petty.Core.Assets;
using Petty.Core.Behavior;

namespace Petty.Tests;

public class AnimationPlaybackTests
{
    private static AnimationClipMetadata Clip(
        string name = "walk",
        int frames = 8,
        double fps = 8,
        bool loop = true) => new()
    {
        Name = name,
        StartIndex = 0,
        FrameCount = frames,
        FramesPerSecond = fps,
        Loop = loop,
    };

    [Fact]
    public void StartsOnFrameZero()
    {
        var playback = new AnimationPlayback();
        Assert.True(playback.Play(Clip()));
        Assert.Equal(0, playback.FrameIndex);
    }

    [Fact]
    public void AdvancesOneFramePerFrameDuration()
    {
        var playback = new AnimationPlayback();
        playback.Play(Clip(frames: 4, fps: 10));

        Assert.False(playback.Advance(TimeSpan.FromMilliseconds(90)));
        Assert.Equal(0, playback.FrameIndex);

        Assert.True(playback.Advance(TimeSpan.FromMilliseconds(20)));
        Assert.Equal(1, playback.FrameIndex);

        Assert.True(playback.Advance(TimeSpan.FromMilliseconds(100)));
        Assert.Equal(2, playback.FrameIndex);
    }

    [Fact]
    public void LoopingClipWrapsToTheStart()
    {
        var playback = new AnimationPlayback();
        playback.Play(Clip(frames: 4, fps: 10));

        playback.Advance(TimeSpan.FromMilliseconds(400));

        Assert.Equal(0, playback.FrameIndex);
        Assert.False(playback.IsFinished);
    }

    [Fact]
    public void NonLoopingClipHoldsTheLastFrame()
    {
        var playback = new AnimationPlayback();
        playback.Play(Clip(name: "exit", frames: 6, fps: 10, loop: false));

        Assert.True(playback.Advance(TimeSpan.FromMilliseconds(700)));
        Assert.Equal(5, playback.FrameIndex);
        Assert.True(playback.IsFinished);

        Assert.False(playback.Advance(TimeSpan.FromSeconds(1)));
        Assert.Equal(5, playback.FrameIndex);
    }

    [Fact]
    public void SameClipDoesNotRestartUnlessAsked()
    {
        var playback = new AnimationPlayback();
        var clip = Clip();
        playback.Play(clip);
        playback.Advance(TimeSpan.FromMilliseconds(200));

        Assert.False(playback.Play(Clip())); // same name
        Assert.Equal(1, playback.FrameIndex);

        Assert.True(playback.Play(Clip(), restart: true));
        Assert.Equal(0, playback.FrameIndex);
    }

    [Fact]
    public void DifferentClipRestarts()
    {
        var playback = new AnimationPlayback();
        playback.Play(Clip("idle", frames: 4, fps: 5));
        playback.Advance(TimeSpan.FromSeconds(1));

        Assert.True(playback.Play(Clip("walk", frames: 8, fps: 9)));
        Assert.Equal(0, playback.FrameIndex);
    }

    [Fact]
    public void LargeDeltaStaysWithinFrameRange()
    {
        var playback = new AnimationPlayback();
        playback.Play(Clip(frames: 6, fps: 7));

        playback.Advance(TimeSpan.FromMinutes(5));

        Assert.InRange(playback.FrameIndex, 0, 5);
    }

    [Fact]
    public void NegativeOrZeroDeltaIsIgnored()
    {
        var playback = new AnimationPlayback();
        playback.Play(Clip(frames: 4, fps: 10));

        Assert.False(playback.Advance(TimeSpan.Zero));
        Assert.False(playback.Advance(TimeSpan.FromMilliseconds(-50)));
        Assert.Equal(0, playback.FrameIndex);
    }

    [Fact]
    public void LoopOverrideKeepsANonLoopingClipRunning()
    {
        var playback = new AnimationPlayback();
        playback.Play(Clip(name: "exit", frames: 6, fps: 10, loop: false), loop: true);

        playback.Advance(TimeSpan.FromMilliseconds(700));

        Assert.True(playback.IsLooping);
        Assert.Equal(1, playback.FrameIndex); // 7 frames in, wrapped
        Assert.False(playback.IsFinished);
    }

    [Theory]
    [InlineData(PetActivity.Entering, "walk")]
    [InlineData(PetActivity.Walking, "walk")]
    [InlineData(PetActivity.SeekingFood, "walk")]
    [InlineData(PetActivity.Running, "run")]
    [InlineData(PetActivity.Eating, "eat")]
    [InlineData(PetActivity.Sleeping, "sleep")]
    [InlineData(PetActivity.Playing, "play")]
    [InlineData(PetActivity.Tired, "tired")]
    [InlineData(PetActivity.Exiting, "exit")]
    [InlineData(PetActivity.Idle, "idle")]
    public void BehaviourStatesMapToClips(PetActivity activity, string expected)
    {
        Assert.Equal(expected, PetAnimationMap.ClipFor(activity));
    }
}
