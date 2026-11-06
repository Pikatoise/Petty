using System.Windows.Interop;
using Petty.App.Delivery;
using Petty.App.Desktop;
using Petty.App.Sprites;
using Petty.App.Views;
using Petty.Core.Diagnostics;

internal static partial class Program
{
    private static void CheckPetWindowPlacement()
    {
        var log = new PlacementVerificationLog();
        SpriteSheet sheet = PostmanCoordinator.LoadMailSheet(null);
        var window = new PetWindow(sheet, log);
        nint handle = new WindowInteropHelper(window).EnsureHandle();
        HwndSource source = HwndSource.FromHwnd(handle)!;
        int nativePlacements = 0;
        nint CountPlacement(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message == 0x0046) // WM_WINDOWPOSCHANGING counts actual native placement attempts.
            {
                nativePlacements++;
            }
            return nint.Zero;
        }
        source.AddHook(CountPlacement);
        try
        {
            window.MoveTo(100, 1000);
            int placed = nativePlacements;
            Require(placed > 0, "Placement verification did not observe its hidden HWND.");
            for (int i = 0; i < 1000; i++)
            {
                window.MoveTo(100.1, 1000.1);
            }
            Require(nativePlacements == placed, "Unchanged rounded pet geometry still invokes native placement.");

            window.SetFacing(true);
            window.ShowFrame(sheet.GetClip("mail-idle")[1]);
            window.MoveTo(100.1, 1000.1);
            Require(nativePlacements == placed, "Facing or frame updates invalidated unchanged geometry.");

            window.MoveTo(101, 1000);
            Require(nativePlacements > placed && window.PlacementRect.Left == 101, "Walking lost physical pixel movement.");
            placed = nativePlacements;
            int oldTop = window.PlacementRect.Top;
            window.MoveTo(101, 999);
            Require(nativePlacements > placed && window.PlacementRect.Top == oldTop - 1, "Floor easing lost physical pixel movement.");

            window.SetSpriteScale(4);
            placed = nativePlacements;
            window.MoveTo(101, 999);
            Require(nativePlacements > placed && window.PlacementRect.Width == 256, "Sprite scaling did not replace its native geometry.");
            window.AttachToDesktop(DesktopLayerMode.Disabled, log);
            placed = nativePlacements;
            window.MoveTo(101, 999);
            Require(nativePlacements > placed, "Desktop reattachment did not force placement.");

            Require(DesktopWindowService.PlaceAt(handle, 400, 500, 256, 256), "External-move verification failed.");
            window.MoveTo(101, 999);
            Require(window.PlacementRect.Left == 101, "An external native move was not repaired.");
        }
        finally
        {
            source.RemoveHook(CountPlacement);
            window.Close();
        }
    }

    private sealed class PlacementVerificationLog : IAppLog
    {
        public void Info(string message)
        {
        }
        public void Warn(string message)
        {
        }
        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
