using Petty.Core.Animation;

namespace Petty.App.Sprites;

/// <summary>
/// Connects the WPF-free <see cref="AnimationPlayback"/> cursor to decoded frames.
/// Switching frames only raises <see cref="FrameChanged"/> when the visible frame
/// actually changed, which keeps the number of UI updates low.
/// </summary>
public sealed class AnimationController
{
    private readonly SpriteSheet _sheet;
    private readonly AnimationPlayback _playback = new();

    public AnimationController(SpriteSheet sheet)
    {
        _sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
    }

    public SpriteClip? CurrentClip { get; private set; }

    public SpriteFrame? CurrentFrame { get; private set; }

    public event EventHandler? FrameChanged;

    /// <summary>
    /// Plays the clip for a clip name. The exit clip is forced to loop while the pet
    /// keeps walking off-screen; otherwise the metadata's loop flag is used.
    /// </summary>
    public bool Play(string clipName)
    {
        if (!_sheet.TryGetClip(clipName, out var clip)) return false;

        var loop = string.Equals(clip.Name, PetAnimationMap.Exit, StringComparison.OrdinalIgnoreCase)
            ? true
            : (bool?)null;

        var changed = _playback.Play(clip.Metadata, restart: false, loop: loop);

        if (changed || CurrentFrame is null)
        {
            CurrentClip = clip;
            SetFrame(clip[0]);
        }

        return changed;
    }

    public bool Advance(TimeSpan delta)
    {
        if (CurrentClip is null) return false;
        if (!_playback.Advance(delta)) return false;

        SetFrame(CurrentClip[_playback.FrameIndex]);
        return true;
    }

    private void SetFrame(SpriteFrame frame)
    {
        CurrentFrame = frame;
        FrameChanged?.Invoke(this, EventArgs.Empty);
    }
}
