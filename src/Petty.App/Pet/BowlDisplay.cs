using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Petty.App.Desktop;
using Petty.App.Sprites;
using Petty.App.Views;
using Petty.Core.Diagnostics;
using Petty.Core.Runtime;

namespace Petty.App.Pet;

/// <summary>Owns bowl images and the click-through pouring effect for one session.</summary>
internal sealed class BowlDisplay : IDisposable
{
    private readonly PropWindow _bowl;
    private readonly PropWindow _effect;
    private readonly DesktopSurfaceController _surface;
    private readonly PropImage[] _levels;
    private readonly PropImage[] _frames;
    private readonly BowlPresentation _pour = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Stopwatch _clock = new();
    private TimeSpan _lastTick;
    private int _level = -1;

    public BowlDisplay(PropWindow bowl, DesktopSurfaceController surface, string assets, DesktopLayerMode mode, IAppLog log)
    {
        _bowl = bowl;
        _surface = surface;
        _levels = new[] { "bowl-empty.png", "bowl-25.png", "bowl-50.png", "bowl-75.png", "bowl-full.png" }
            .Select(name => PropImage.Load(Path.Combine(assets, name))).ToArray();
        _frames = Enumerable.Range(0, 6).Select(i => PropImage.Load(Path.Combine(assets, $"pour-{i}.png"))).ToArray();
        _effect = new PropWindow(_frames[0], interactive: false, log);
        _effect.Show();
        _effect.AttachToDesktop(mode, log);
        _effect.Hide();
        _timer.Tick += OnTick;
    }

    public bool TryPour(double food, double capacity)
    {
        if (!_pour.TryStart(food, capacity)) return false;
        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _effect.ShowImage(_frames[0]);
        PlaceEffect();
        _effect.Show();
        _timer.Start();
        return true;
    }

    public void UpdateLevel(double food, double capacity)
    {
        var level = BowlPresentation.Level(food, capacity);
        if (level == _level) return;
        _level = level;
        _bowl.ShowImage(_levels[level]);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = _clock.Elapsed;
        var frame = _pour.Advance(now - _lastTick);
        _lastTick = now;
        if (!_pour.IsPouring)
        {
            _timer.Stop();
            _effect.Hide();
            return;
        }
        _effect.ShowImage(_frames[frame]);
        PlaceEffect();
    }

    private void PlaceEffect()
    {
        var bowl = _bowl.ScreenRect;
        var left = (double)bowl.Left;
        var top = (double)bowl.Top - _effect.PhysicalHeight + 39;
        if (_surface.PrimaryDisplay is { } display)
        {
            var bounds = display.UsableArea;
            left = Math.Clamp(left, bounds.X, Math.Max(bounds.X, bounds.Right - _effect.PhysicalWidth));
            top = Math.Clamp(top, bounds.Y, Math.Max(bounds.Y, bounds.Bottom - _effect.PhysicalHeight));
        }
        _effect.PlaceAt(left, top);
    }

    public void Stop()
    {
        _timer.Stop();
        _pour.Stop();
        _clock.Stop();
        _effect.Hide();
    }

    public void Dispose()
    {
        Stop();
        _timer.Tick -= OnTick;
        _effect.Close();
    }
}
