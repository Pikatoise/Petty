using Petty.Core.Domain;

namespace Petty.Core.Runtime;

/// <summary>Places a menu against an opaque sprite point, in physical screen pixels.</summary>
public static class InteractionMenuPlacement
{
    /// <summary>Keep compact-display actions readable, and follow large pets without applying DPI twice.</summary>
    public static double ScaleForDisplay(double spriteScale, double dpiScale) =>
        Math.Max(Math.Clamp(SpriteDisplayScale.ReferenceScale / spriteScale, 1, 1.35),
            spriteScale / (SpriteDisplayScale.ReferenceScale * Math.Max(1, dpiScale)));

    public static bool IsPetHovered(ScreenRect pet, double x, double y, bool isExposed) =>
        isExposed && x >= pet.X && x < pet.Right && y >= pet.Y && y < pet.Bottom;

    public static ScreenRect Place(ScreenRect usable, double anchorX, double anchorY, double width, double height)
    {
        width = Math.Min(Math.Max(1, width), usable.Width);
        height = Math.Min(Math.Max(1, height), usable.Height);
        double x = anchorX - width / 2;
        double y = anchorY - height + 1;
        if (y < usable.Y)
        {
            if (anchorX + width - 1 <= usable.Right)
            {
                x = anchorX - 1;
                y = anchorY - height / 2;
            }
            else if (anchorX - width + 1 >= usable.X)
            {
                x = anchorX - width + 1;
                y = anchorY - height / 2;
            }
            else
            {
                x = anchorX - width / 2;
                y = anchorY - 1;
            }
        }
        return new ScreenRect(Math.Clamp(x, usable.X, usable.Right - width),
            Math.Clamp(y, usable.Y, usable.Bottom - height), width, height);
    }
}
