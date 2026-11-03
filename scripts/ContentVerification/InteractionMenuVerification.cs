using System.IO;
using System.Windows;
using System.Windows.Controls;
using Petty.App.Views;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using Petty.Core.Runtime;

internal static partial class Program
{
    private static void CheckInteractionLayout(string output, string language)
    {
        var log = new FileAppLog(Path.Combine(output, "interaction-layout.log"));
        var menu = new InteractionMenuWindow(log);
        Render(menu, Path.Combine(output, $"interaction-initial-{language}.png"));
        foreach ((int width, int height, double dpi) in new[] { (1280, 720, 1.0), (1920, 1080, 1.0), (2560, 1600, 1.5), (3840, 2160, 2.0) })
        {
            double scale = InteractionMenuPlacement.ScaleForDisplay(SpriteDisplayScale.ForDisplay(new ScreenRect(0, 0, width, height)), dpi);
            menu.Width = InteractionMenuWindow.MenuWidth * scale;
            menu.Height = InteractionMenuWindow.MenuHeight * scale;
            Render(menu, Path.Combine(output, $"interaction-{width}x{height}-{language}.png"));
            var content = (FrameworkElement)menu.Content;
            foreach (string name in new[] { "PlayButton", "PetButton" })
            {
                var button = (Button)menu.FindName(name);
                Rect bounds = button.TransformToAncestor(content).TransformBounds(new Rect(button.RenderSize));
                Require(bounds.Width > 0 && bounds.Height > 0 && bounds.Left >= 0 && bounds.Top >= 0 &&
                    bounds.Right <= content.ActualWidth + 1 && bounds.Bottom <= content.ActualHeight + 1,
                    $"Interaction action clipped at {width}x{height}/{dpi} DPI.");
            }
        }
        menu.Close();
    }
}
