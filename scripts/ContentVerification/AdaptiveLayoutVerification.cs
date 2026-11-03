using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Petty.App;
using Petty.App.Sprites;
using Petty.App.Views;
using Petty.Core.Diagnostics;
using Petty.Core.Preferences;

internal static class AdaptiveLayoutVerification
{
    private static readonly MethodInfo Fit = typeof(App).Assembly.GetType("Petty.App.Views.AdaptiveWindowLayout")!
        .GetMethod("Fit", BindingFlags.Static | BindingFlags.NonPublic)!;

    public static void Check(IReadOnlyList<SpriteVariant> variants, string output, string language)
    {
        Type localizationType = typeof(App).Assembly.GetType("Petty.App.Localization")!;
        object localization = Activator.CreateInstance(localizationType,
            new PreferencesStore(Path.Combine(output, "layout-preferences.json")), new UserPreferences(language))!;
        var settings = (SettingsWindow)Activator.CreateInstance(typeof(SettingsWindow),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            ["Milo", "cat", variants[0].BreedId, localization, new VerificationLog()], null)!;
        var wizard = new CreationWizardWindow(variants, true, "Milo", null);
        Window[] windows = [settings, wizard, new ConfirmNewPetWindow(), new TrayPopupWindow()];
        try
        {
            foreach (Window window in windows)
            {
                double preferred = window.Width;
                foreach ((string name, Size area) in new[]
                {
                    ("fhd", new Size(1904, 1024)),
                    // 4K at 200% has the same DIP budget as Full HD at 100%.
                    ("4k-200", new Size(1904, 1024)),
                    ("compact", new Size(340, 260)),
                })
                {
                    Fit.Invoke(null, [window, area, preferred]);
                    if (window is CreationWizardWindow creator)
                    {
                        typeof(CreationWizardWindow).GetMethod("UpdateColumns", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(creator, null);
                        int row = Grid.GetRow((StackPanel)creator.FindName("FormPanel"));
                        Require(row == (name == "compact" ? 1 : 0), "Creator did not adapt its preview/form columns.");
                    }
                    Require(window.MaxHeight <= area.Height && window.MaxWidth <= area.Width, "Window exceeds its DIP work area.");
                    if (double.IsFinite(preferred))
                    {
                        Require(window.Width == Math.Min(preferred, area.Width), "Normal window size changed or did not shrink.");
                    }

                    var content = (FrameworkElement)window.Content;
                    double width = double.IsFinite(window.Width) ? window.Width - 16 : Math.Min(200, area.Width);
                    double height = Math.Min(area.Height - (window.WindowStyle == WindowStyle.None ? 0 : 40), 740);
                    content.Measure(new Size(width, height));
                    height = Math.Min(height, content.DesiredSize.Height);
                    content.Arrange(new Rect(0, 0, width, height));
                    content.UpdateLayout();
                    ScrollViewer scroll = FindScroll(content);
                    if (name == "compact" && window is SettingsWindow or CreationWizardWindow)
                    {
                        Require(scroll.ScrollableHeight > 0, "Constrained dialog did not make its lower controls scrollable.");
                        scroll.ScrollToEnd();
                        content.UpdateLayout();
                        Require(scroll.VerticalOffset > 0, "Constrained dialog cannot scroll to its final actions.");
                        Save(content, window.Background, Path.Combine(output, $"layout-{language}-{window.GetType().Name}-{name}-bottom.png"));
                        scroll.ScrollToHome();
                        content.UpdateLayout();
                    }
                    Save(content, window.Background, Path.Combine(output, $"layout-{language}-{window.GetType().Name}-{name}.png"));
                }
            }
        }
        finally
        {
            foreach (Window window in windows)
            {
                window.Close();
            }
            var strings = (ResourceDictionary)localizationType.GetField("_strings", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(localization)!;
            Application.Current.Resources.MergedDictionaries.Remove(strings);
        }
    }

    private static ScrollViewer FindScroll(DependencyObject root)
    {
        if (root is ScrollViewer scroll)
        {
            return scroll;
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer found)
            {
                return found;
            }
        }
        throw new InvalidOperationException("Adaptive dialog has no scrolling content.");
    }

    private static void Save(FrameworkElement content, Brush background, string path)
    {
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (DrawingContext drawing = backdrop.RenderOpen())
        {
            drawing.DrawRectangle(background, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        }
        image.Render(backdrop);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class VerificationLog : IAppLog
    {
        public void Info(string message)
        {
        }
        public void Warn(string message)
        {
        }
        public void Error(string message, Exception? exception = null) => throw new InvalidOperationException(message, exception);
    }
}
