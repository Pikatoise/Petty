using Petty.Core.Behavior;

namespace Petty.Core.Runtime;

public readonly record struct PlayScenePose(int FrameIndex, double PropOffset, double PropHeight);

/// <summary>Deterministic prop trajectories, in physical pixels relative to the pet.</summary>
public static class ActionScene
{
    public static (double PropOffset, double PropHeight) Sample(PetActivity activity, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        if (activity is PetActivity.Petting)
        {
            var approach = Math.Min(1, t * 5);
            var retreat = Math.Min(1, (1 - t) * 5);
            return (Math.Sin(t * Math.PI * 6) * 8 * Math.Min(approach, retreat), -35 * (1 - Math.Min(approach, retreat)));
        }
        var play = SamplePlay(t * 6, 6, 12, 10);
        return (play.PropOffset, play.PropHeight);
    }

    /// <summary>
    /// The shared play clip extends the paw at one quarter and three quarters of
    /// its cycle (frames 3 and 9 in the installed 12-frame clips). Ball contacts
    /// and the displayed sprite frame come from this same absolute scene clock.
    /// </summary>
    public static PlayScenePose SamplePlay(double elapsedSeconds, double durationSeconds, int frameCount, double fps,
        double exitDistance = 640, double contactLift = 0)
    {
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0 || frameCount <= 0 || !double.IsFinite(fps) || fps <= 0 ||
            !double.IsFinite(exitDistance) || exitDistance <= 0 || !double.IsFinite(contactLift) || contactLift < 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Unusable play scene parameters.");
        var elapsed = double.IsFinite(elapsedSeconds) ? Math.Clamp(elapsedSeconds, 0, durationSeconds) : 0;
        var beat = Math.Min(frameCount / (2 * fps), durationSeconds * 0.4);
        var firstHit = beat / 2;
        var rollout = Math.Min(durationSeconds * 0.4, Math.Max(beat * 2, durationSeconds / 3));
        var lastHit = firstHit + Math.Max(0, Math.Floor((durationSeconds - rollout - firstHit) / beat + 1e-9)) * beat;
        var spriteTime = Math.Min(elapsed, lastHit + beat / 2);
        var frame = (int)Math.Floor(spriteTime * frameCount / (2 * beat) + 1e-9) % frameCount;

        if (elapsed < firstHit)
        {
            var approach = Smooth(elapsed / firstHit);
            return new(frame, 64 * (1 - approach), -contactLift * approach);
        }
        if (elapsed >= lastHit)
        {
            var u = (elapsed - lastHit) / (durationSeconds - lastHit);
            // Accelerate out of the final paw contact, then roll past the display
            // edge at nonzero speed; don't stop or disappear in the walk band.
            var offset = exitDistance * u * u * (2 - u);
            var hop = u < 0.35 ? Math.Pow(Math.Sin(Math.PI * u / 0.35), 2) * 7 : 0;
            return new(frame, offset, -contactLift * (1 - Smooth(Math.Min(1, u / 0.35))) - hop);
        }

        var phase = (elapsed - firstHit) / beat;
        phase -= Math.Floor(phase);
        // Ease away and back between paw extensions. Small, short hops settle
        // into rolling instead of a large sinusoidal sweep across the desktop.
        var roll = Math.Pow(Math.Sin(Math.PI * phase), 2);
        var bounce = phase < 0.5 ? Math.Pow(Math.Sin(Math.PI * phase * 2), 2) * 5 : 0;
        return new(frame, 36 * roll, -contactLift * (1 - roll) - bounce);
    }

    private static double Smooth(double t) => t * t * t * (10 + t * (-15 + 6 * t));
}
