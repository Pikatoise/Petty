using Petty.Core.Domain;

namespace Petty.Core.Runtime;

/// <summary>Scales continuously between the 1280x720 baseline and 2560x1600 reference display.</summary>
public static class SpriteDisplayScale
{
    public const int ReferenceScale = 3;

    public static double ForDisplay(ScreenRect bounds)
    {
        if (!bounds.HasArea)
        {
            return ReferenceScale;
        }

        double growth = Math.Min((bounds.Width - 1280) / 1280, (bounds.Height - 720) / 880);
        return Math.Max(1, 1 + 2 * growth);
    }
}
