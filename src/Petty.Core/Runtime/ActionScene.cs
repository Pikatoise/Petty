using Petty.Core.Behavior;

namespace Petty.Core.Runtime;

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
        // The ball approaches the paw, is tapped twice, then rolls away.
        var offset = t < 0.25 ? 55 - 100 * t :
            t < 0.5 ? 30 + 70 * Math.Sin((t - 0.25) * Math.PI * 4) :
            t < 0.7 ? 30 + 45 * Math.Sin((t - 0.5) * Math.PI * 5) :
            30 + 600 * Math.Pow((t - 0.7) / 0.3, 2);
        return (offset, 0);
    }
}
