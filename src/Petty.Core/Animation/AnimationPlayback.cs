using Petty.Core.Assets;

namespace Petty.Core.Animation;

/// <summary>
/// Time-based frame cursor over one animation clip. It advances from elapsed time
/// rather than from render frames, so playback speed stays correct even if a frame
/// is dropped or the UI timer drifts.
/// </summary>
public sealed class AnimationPlayback
{
    private double _elapsedSeconds;
    private AnimationClipMetadata? _clip;
    private bool? _loopOverride;

    public AnimationClipMetadata? Clip => _clip;

    public int FrameIndex { get; private set; }

    public bool IsFinished { get; private set; }

    public double FramesPerSecond => _clip?.FramesPerSecond ?? 0;

    /// <summary>Looping state; an explicit override wins over the clip metadata.</summary>
    public bool IsLooping => _loopOverride ?? _clip?.Loop ?? false;

    /// <summary>
    /// Selects a clip. Switching clips always restarts at frame 0; requesting the
    /// clip that is already playing only restarts when <paramref name="restart"/> is true.
    /// </summary>
    public bool Play(AnimationClipMetadata clip, bool restart = false, bool? loop = null)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (clip.FrameCount <= 0) throw new ArgumentException("Clip has no frames.", nameof(clip));

        var sameClip = _clip is not null &&
                       string.Equals(_clip.Name, clip.Name, StringComparison.OrdinalIgnoreCase);

        _loopOverride = loop;

        if (sameClip && !restart) return false;

        _clip = clip;
        _elapsedSeconds = 0;
        FrameIndex = 0;
        IsFinished = false;
        return true;
    }

    /// <summary>Advances the frame cursor. Returns true when the displayed frame changed.</summary>
    public bool Advance(TimeSpan delta)
    {
        if (_clip is null || delta <= TimeSpan.Zero) return false;
        if (IsFinished) return false;

        _elapsedSeconds += delta.TotalSeconds;

        var fps = Math.Max(0.001, _clip.FramesPerSecond);
        var rawFrame = (int)Math.Floor(_elapsedSeconds * fps);

        int next;
        if (IsLooping)
        {
            next = rawFrame % _clip.FrameCount;
        }
        else
        {
            next = Math.Min(_clip.FrameCount - 1, rawFrame);
            if (rawFrame >= _clip.FrameCount) IsFinished = true;
        }

        if (next == FrameIndex) return false;

        FrameIndex = next;
        return true;
    }

    /// <summary>Jumps to the first frame without changing the clip (used when a state restarts).</summary>
    public void Reset()
    {
        _elapsedSeconds = 0;
        FrameIndex = 0;
        IsFinished = false;
    }
}
