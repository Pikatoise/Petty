using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Petty.App.Sprites;
using Petty.App.Views;

internal sealed class ContentVerificationApp : Petty.App.App
{
    public ContentVerificationApp() => ShutdownMode = ShutdownMode.OnExplicitShutdown;

    public new void InitializeComponent() => Resources.MergedDictionaries.Add(new ResourceDictionary
    {
        Source = new Uri("/Petty;component/Resources/CozyHome.xaml", UriKind.Relative)
    });

    // A dispatcher pump must not run the real application's startup or touch owner data.
    protected override void OnStartup(StartupEventArgs e)
    {
    }
}

internal static partial class Program
{
    private static void CheckTutorialLayout(IReadOnlyList<SpriteVariant> variants, string assets, string output, string language)
    {
        MethodInfo showPage = typeof(TutorialWindow).GetMethod("ShowPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
        string[] pageNames = ["TutorialWelcomePage", "TutorialFeedingPage", "TutorialInteractionPage", "TutorialTrayPage"];
        foreach (bool firstRun in new[] { true, false })
        {
            var window = new TutorialWindow(variants, assets, firstRun);
            try
            {
                var content = (FrameworkElement)window.Content;
                var frame = (Frame)window.FindName("PageFrame");
                var navigation = (Grid)window.FindName("NavigationPanel");
                var header = (FrameworkElement)((TextBlock)window.FindName("PagePosition")).Parent;
                var previous = (Button)window.FindName("PreviousButton");
                var next = (Button)window.FindName("NextButton");
                var skip = (Button)window.FindName("SkipButton");
                var pages = new Page[4];
                Rect? footerBounds = null;
                Rect? headerBounds = null;
                Require(window.Width == 520 && window.Height == 540, "Tutorial must keep its compact fixed window size.");
                Require(!previous.IsEnabled, "First tutorial page must disable Back.");
                ArrangeTutorial(window);
                frame.ApplyTemplate();
                for (int index = 0; index < 4; index++)
                {
                    PumpTutorialNavigation();
                    var page = (Page)frame.Content;
                    pages[index] = page;
                    Require(page.GetType().FullName == $"Petty.App.Views.Tutorial.{pageNames[index]}",
                        "Each tutorial step must navigate to its own Page.");
                    Require(((TextBlock)page.FindName("PageHeading")).Text == Application.Current.FindResource($"TutorialHeading{index + 1}") as string,
                        "Tutorial page has missing or incorrect localized heading.");
                    Require(skip.Visibility == (index == 3 ? Visibility.Collapsed : Visibility.Visible),
                        "Skip must be available before the last page and hidden on the last page.");
                    MethodInfo render = page.GetType().GetMethod("RenderScene", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    for (int sample = 0; sample < 200; sample++)
                    {
                        render.Invoke(page, [sample / 20.0]);
                        Require(((Image)page.FindName("PetImage")).Source is BitmapSource { IsFrozen: true },
                            "Tutorial must use decoded frozen sprite frames.");
                    }
                    foreach (double time in new[] { 0.0, 0.7, 3, 6 })
                    {
                        render.Invoke(page, [time]);
                        RenderTutorial(window, Path.Combine(output, $"tutorial-{language}-{firstRun}-{index + 1}-{time.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}.png"));
                        Rect footer = TutorialBounds(navigation, content);
                        Rect heading = TutorialBounds(header, content);
                        footerBounds ??= footer;
                        headerBounds ??= heading;
                        Require(footer == footerBounds.Value && heading == headerBounds.Value,
                            "Tutorial header and footer must stay fixed while pages and scenes change.");
                        Require(Math.Abs(footer.Height - 44) < 0.1 && footer.Bottom <= content.ActualHeight,
                            "Tutorial navigation must occupy its fixed footer inside the viewport.");
                        CheckTutorialButtons(content, previous, next, skip);
                    }
                    if (index < 3)
                    {
                        next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Require(previous.IsEnabled, "Back must be available after the first page.");
                    }
                }
                Require(((TextBlock)next.Content).Text == Application.Current.FindResource(firstRun ? "CreatePet" : "TutorialClose") as string,
                    "Tutorial completion must reflect first-run versus replay mode.");
                bool requested = false;
                window.CompletionRequested += () => { requested = true; return false; };
                next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(requested && !string.IsNullOrWhiteSpace(((TextBlock)window.FindName("ErrorText")).Text),
                    "Failed completion persistence must keep the tutorial open and explain the problem.");

                window.Width = 340;
                window.Height = 260;
                for (int index = 0; index < 4; index++)
                {
                    showPage.Invoke(window, [index]);
                    PumpTutorialNavigation();
                    Require(ReferenceEquals(frame.Content, pages[index]), "Tutorial navigation must reuse its isolated Page instances.");
                    RenderTutorial(window, Path.Combine(output, $"tutorial-{language}-{firstRun}-{index + 1}-compact.png"));
                    var scroll = (ScrollViewer)pages[index].FindName("PageScroll");
                    Require(scroll.ScrollableHeight > 0, "Compact tutorial content must scroll within its Page.");
                    Rect beforeFooter = TutorialBounds(navigation, content);
                    Rect beforeHeader = TutorialBounds(header, content);
                    scroll.ScrollToEnd();
                    content.UpdateLayout();
                    Require(scroll.VerticalOffset > 0 && TutorialBounds(navigation, content) == beforeFooter &&
                        TutorialBounds(header, content) == beforeHeader,
                        "Scrolling tutorial content must not move the shell header or footer.");
                    Rect viewport = TutorialBounds(scroll, content);
                    Require(viewport.Top >= beforeHeader.Bottom && viewport.Bottom <= beforeFooter.Top,
                        "Page scrolling must remain between the fixed shell header and footer.");
                    CheckTutorialButtons(content, previous, next, skip);
                    RenderTutorial(window, Path.Combine(output, $"tutorial-{language}-{firstRun}-{index + 1}-compact-bottom.png"));
                }
                previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                PumpTutorialNavigation();
                Require(((TextBlock)window.FindName("PagePosition")).Text == "3 / 4" && ReferenceEquals(frame.Content, pages[2]),
                    "Back did not navigate to the preceding Page.");
                showPage.Invoke(window, [0]);
                PumpTutorialNavigation();
                Require(!previous.IsEnabled && ReferenceEquals(frame.Content, pages[0]), "First-page navigation state was not reset.");
            }
            finally
            {
                window.Close();
            }
        }
    }

    private static void PumpTutorialNavigation()
    {
        var dispatcherFrame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => dispatcherFrame.Continue = false);
        Dispatcher.PushFrame(dispatcherFrame);
    }

    private static Rect TutorialBounds(FrameworkElement element, FrameworkElement ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static void CheckTutorialButtons(FrameworkElement content, params Button[] buttons)
    {
        foreach (Button button in buttons.Where(button => button.Visibility == Visibility.Visible))
        {
            Rect bounds = TutorialBounds(button, content);
            Require(bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right <= content.ActualWidth + 1 && bounds.Bottom <= content.ActualHeight + 1,
                "Tutorial navigation escapes the fixed viewport.");
        }
    }

    private static void RenderTutorial(Window window, string path)
    {
        FrameworkElement content = ArrangeTutorial(window);
        double width = Math.Max(1, window.Width - 16);
        double height = Math.Max(1, window.Height - 40);
        var image = new RenderTargetBitmap((int)Math.Ceiling(width * 2), (int)Math.Ceiling(height * 2), 192, 192, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (DrawingContext drawing = backdrop.RenderOpen())
        {
            drawing.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        }
        image.Render(backdrop);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    private static FrameworkElement ArrangeTutorial(Window window)
    {
        var content = (FrameworkElement)window.Content;
        // Reserve the window's title bar and borders even though this fixture creates no HWND.
        double width = Math.Max(1, window.Width - 16);
        double height = Math.Max(1, window.Height - 40);
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        return content;
    }
}
