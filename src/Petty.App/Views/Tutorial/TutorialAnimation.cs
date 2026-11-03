using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Petty.App.Views.Tutorial;

/// <summary>Runs only while its page is loaded in the navigation frame.</summary>
internal sealed class TutorialAnimation
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private readonly Stopwatch _clock = new();

    public TutorialAnimation(FrameworkElement page, Action<double> render)
    {
        _timer.Tick += (_, _) => render(_clock.Elapsed.TotalSeconds);
        page.Loaded += (_, _) =>
        {
            _clock.Restart();
            render(0);
            _timer.Start();
        };
        page.Unloaded += (_, _) =>
        {
            _timer.Stop();
            _clock.Stop();
        };
    }
}
