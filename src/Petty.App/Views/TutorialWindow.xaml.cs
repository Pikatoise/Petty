using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Navigation;
using Petty.App.Sprites;
using Petty.App.Views.Tutorial;

namespace Petty.App.Views;

/// <summary>Hosts independent tutorial pages and fixed navigation chrome.</summary>
public partial class TutorialWindow : Window
{
    private readonly bool _firstRun;
    private readonly Page[] _pages;
    private int _page;

    public TutorialWindow(IReadOnlyList<SpriteVariant> variants, string assetDirectory, bool firstRun)
    {
        _firstRun = firstRun;
        var media = new TutorialMedia(variants, assetDirectory);
        InitializeComponent();
        _pages = [new TutorialWelcomePage(media), new TutorialFeedingPage(media),
            new TutorialInteractionPage(media), new TutorialTrayPage(media)];
        AdaptiveWindowLayout.Attach(this);
        SizeChanged += (_, _) => NextButton.MaxWidth = Width < 400 ? 96 : 180;
        Closed += (_, _) =>
        {
            PageFrame.NavigationService.StopLoading();
            PageFrame.Content = null;
        };
        ShowPage(0);
    }

    public event Func<bool>? CompletionRequested;

    private void OnPrevious(object sender, RoutedEventArgs e) => ShowPage(_page - 1);

    private void OnNext(object sender, RoutedEventArgs e)
    {
        if (_page == _pages.Length - 1)
        {
            OnFinish(sender, e);
        }
        else
        {
            ShowPage(_page + 1);
        }
    }

    private void OnFinish(object sender, RoutedEventArgs e)
    {
        if (CompletionRequested?.Invoke() ?? !_firstRun)
        {
            DialogResult = true;
        }
        else
        {
            ErrorText.SetResourceReference(TextBlock.TextProperty, "PreferencesError");
        }
    }

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, _pages.Length - 1);
        PageFrame.Navigate(_pages[_page]);
        PagePosition.Text = $"{_page + 1} / {_pages.Length}";
        PreviousButton.IsEnabled = _page > 0;
        bool last = _page == _pages.Length - 1;
        SkipButton.Visibility = last ? Visibility.Collapsed : Visibility.Visible;
        string nextKey = last ? (_firstRun ? "CreatePet" : "TutorialClose") : "TutorialNext";
        NextButton.SetResourceReference(StyleProperty, last ? "CozyPrimaryButton" : "TutorialArrowButton");
        NextButton.Width = last ? double.NaN : 40;
        NextButton.Padding = last ? new Thickness(12, 4, 12, 4) : new Thickness(0);
        NextButton.Content = new TextBlock
        {
            Text = last ? Localization.Text(nextKey) : "→",
            FontSize = last ? 14 : 20,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center
        };
        NextButton.SetResourceReference(ToolTipProperty, nextKey);
        NextButton.SetResourceReference(AutomationProperties.NameProperty, nextKey);
    }

    private void OnPageNavigated(object sender, NavigationEventArgs e)
    {
        // The shell arrows own the four-step sequence, including backward navigation.
        while (PageFrame.NavigationService.RemoveBackEntry() is not null)
        {
        }
        if (e.Content is Page page && page.FindName("PageScroll") is ScrollViewer scroll)
        {
            scroll.ScrollToHome();
        }
    }
}
