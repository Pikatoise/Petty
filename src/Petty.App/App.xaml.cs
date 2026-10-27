using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Petty.App.Desktop;
using Petty.App.Pet;
using Petty.App.Sprites;
using Petty.App.Views;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using Petty.Core.Persistence;
using Petty.Core.Runtime;

namespace Petty.App;

/// <summary>
/// Application composition root. Stage 3 starts the pet directly with a provisionally
/// loaded pet; stage 4 adds the tray icon, the first-run wizard and the settings
/// window around this same session.
/// </summary>
public partial class App : Application
{
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    private readonly DisplayService _displayService = new();

    private AppOptions _options = new();
    private PetPaths? _paths;
    private string? _assetDirectory;
    private IReadOnlyList<DisplayInfo> _displayLayout = Array.Empty<DisplayInfo>();
    private FileAppLog? _log;
    private PetStateStore? _store;
    private PetRuntime? _runtime;
    private SpriteSheet? _sheet;
    private PetWindow? _window;
    private PetSession? _session;
    private DispatcherTimer? _exitTimer;
    private bool _stateWasPersisted;
    private bool _shutdownStarted;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            _options = AppOptions.Parse(e.Args);
            _paths = PetPaths.Resolve(_options.DataDirectory);
            _log = new FileAppLog(_paths.LogFile);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Petty could not start:" + Environment.NewLine + Environment.NewLine + ex.Message,
                "Petty",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        _log.Info($"Petty starting (pid {Environment.ProcessId}). Data directory: {_paths.DataDirectory}");

        try
        {
            StartPet();
        }
        catch (Exception ex)
        {
            _log.Error("Startup failed.", ex);
            MessageBox.Show(
                "Petty could not start." + Environment.NewLine + Environment.NewLine +
                ex.Message + Environment.NewLine + Environment.NewLine +
                "Technical details were written to:" + Environment.NewLine + _log.FilePath,
                "Petty",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitTimer?.Stop();
        _session?.Dispose();
        _session = null;
        _window = null;
        _sheet = null;
        _log?.Info("Petty stopped.");
        base.OnExit(e);
    }

    private void StartPet()
    {
        var now = DateTimeOffset.UtcNow;
        var tuning = PetTuning.Default.Sanitized();

        _store = new PetStateStore(_paths!.StateFile, tuning);
        var load = _store.Load();
        _log!.Info($"State: {load.Status}. {load.Message}".TrimEnd());

        PetRuntime runtime;

        if (load.Snapshot is not null)
        {
            _stateWasPersisted = true;
            runtime = PetRuntime.Restore(load.Snapshot, tuning, now);
            _log.Info($"Restored '{runtime.Snapshot.PetName}' (satiety {runtime.Needs.Satiety:F0}, happiness {runtime.Needs.Happiness:F0}).");
        }
        else
        {
            // Deliberately not saved: the first-run wizard (stage 4) must still see a
            // missing state file. Until then this provisional pet keeps the API alive.
            runtime = PetRuntime.CreateNew("Kitty", tuning, now);
            _log.Warn("No usable pet state; running a provisional pet without saving it (the creation wizard arrives in stage 4).");
        }

        _runtime = runtime;

        var assetDirectory = SpriteAssetLocator.FindDirectory()
            ?? throw new FileNotFoundException(
                "The cat sprite atlas was not found. Expected 'Assets/Sprites/cat-orange-white.json' " +
                "next to the application, or the repository's 'assets/sprites' folder.");

        _sheet = SpriteSheetLoader.Load(assetDirectory);
        _log.Info($"Loaded '{_sheet.Metadata.Character}': {_sheet.Clips.Count} clips, " +
                  $"{_sheet.FrameWidth}x{_sheet.FrameHeight} frames, ground row {_sheet.GroundRow}.");

        _window = new PetWindow(_sheet, _log);
        _window.Show();
        _window.AttachToDesktop(_options.DesktopLayer, _log);

        var displays = _displayService.GetDisplays();
        _assetDirectory = assetDirectory;
        _displayLayout = displays;

        _session = new PetSession(runtime, _sheet, _window, _displayService, tuning, _log);
        _session.ExitAnimationCompleted += OnExitAnimationCompleted;
        _session.Start();

        // WPF finishes applying the window styles after the first frame, so the style
        // audit waits until the dispatcher is idle instead of reporting a false alarm.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () => DesktopWindowService.VerifyPetWindowStyles(_window.Handle, _log));

        WriteReport(_paths.StartupReportFile, "startup");

        if (_options.ExitAfter is { } delay)
        {
            _log.Info($"Automatic exit scheduled in {delay.TotalSeconds:F1}s (diagnostic switch).");
            _exitTimer = new DispatcherTimer { Interval = delay };
            _exitTimer.Tick += OnExitTimerTick;
            _exitTimer.Start();
        }
    }

    private void OnExitTimerTick(object? sender, EventArgs e)
    {
        _exitTimer?.Stop();

        if (_session is null || !_session.RequestExit())
            FinishAndShutdown();
    }

    private void OnExitAnimationCompleted(object? sender, EventArgs e)
    {
        FinishAndShutdown();
    }

    private void FinishAndShutdown()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;

        _exitTimer?.Stop();
        _log?.Info("Shutdown requested.");

        if (_paths is not null) WriteReport(_paths.FinalReportFile, "final");

        _session?.Dispose();
        _session = null;

        if (_stateWasPersisted && _runtime is not null && _store is not null)
        {
            try
            {
                _store.Save(_runtime.ToSnapshot());
                _log?.Info("Pet state saved.");
            }
            catch (Exception ex)
            {
                _log?.Error("Could not save pet state.", ex);
            }
        }

        _window?.Close();
        _window = null;

        Shutdown();
    }

    /// <summary>
    /// Writes a machine-readable diagnostics snapshot. It is produced at startup and
    /// again at shutdown (<paramref name="phase"/> = "final") so the window geometry
    /// and hit-testing can be audited after a real run.
    /// </summary>
    private void WriteReport(string path, string phase)
    {
        if (_log is null || _sheet is null || _window is null || _runtime is null || _assetDirectory is null) return;

        try
        {
            var attachment = _window.Attachment;
            var displays = _displayLayout.Count > 0 ? _displayLayout : _displayService.GetDisplays();
            var display = _displayService.Primary(displays);
            var rect = _window.PlacementRect;

            var hitTests = DesktopWindowService.ProbeHitTest(_window.Handle, new[]
            {
                (rect.Left + 3, rect.Top + 3),
                (rect.Left + (rect.Width / 2), rect.Top + (rect.Height / 2)),
                (rect.Left + (rect.Width / 2), rect.Top + rect.Height - 6),
            });

            var report = new
            {
                generatedUtc = DateTimeOffset.UtcNow,
                phase,
                pet = new
                {
                    name = _runtime.Snapshot.PetName,
                    activity = _runtime.Behavior.State.ToString(),
                    satiety = Math.Round(_runtime.Needs.Satiety, 1),
                    happiness = Math.Round(_runtime.Needs.Happiness, 1),
                },
                assets = new
                {
                    directory = _assetDirectory,
                    character = _sheet.Metadata.Character,
                    frameSize = $"{_sheet.FrameWidth}x{_sheet.FrameHeight}",
                    clips = _sheet.Clips.Count,
                    groundRow = _sheet.GroundRow,
                    scale = PetWindow.ScreenPixelsPerSpritePixel,
                },
                desktop = new
                {
                    mode = attachment.Mode.ToString(),
                    hostClass = attachment.HostClass,
                    hostHandle = $"0x{attachment.HostHandle:X}",
                    detail = attachment.Detail,
                    requestedLayer = _options.DesktopLayer.ToString(),
                    candidates = DesktopWindowService.LastCandidates,
                },
                window = DesktopWindowService.Describe(_window.Handle),
                placement = new
                {
                    floorY = Math.Round(_window.PlacementFloorY, 1),
                    expectedTop = Math.Round(_window.ExpectedTop, 1),
                    hitTests,
                },
                walkArea = new
                {
                    left = Math.Round(_session?.WalkArea.Left ?? 0, 1),
                    right = Math.Round(_session?.WalkArea.Right ?? 0, 1),
                    floorY = Math.Round(_session?.WalkArea.FloorY ?? 0, 1),
                },
                taskbar = _session?.ShellBar is { } bar
                    ? new
                    {
                        handle = $"0x{bar.Handle:X}",
                        className = bar.ClassName,
                        rect = FormatRect(bar.Rect),
                        revealed = bar.Revealed,
                        note = bar.Note,
                    }
                    : null,
                display = new
                {
                    device = display.DeviceName,
                    primary = display.IsPrimary,
                    bounds = FormatRect(display.Bounds),
                    workArea = FormatRect(display.WorkArea),
                    dpiScale = _window.DpiScale,
                },
            };

            File.WriteAllText(path, JsonSerializer.Serialize(report, ReportJsonOptions));
            _log.Info($"Diagnostics report ({phase}) written to {path}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn($"Could not write the diagnostics report ({phase}): {ex.Message}");
        }
    }

    private static string FormatRect(ScreenRect rect) => $"{rect.X},{rect.Y} {rect.Width}x{rect.Height}";

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("Unhandled UI exception.", e.Exception);
        e.Handled = true;

        MessageBox.Show(
            "Petty hit an unexpected error and will close." + Environment.NewLine + Environment.NewLine +
            e.Exception.Message + Environment.NewLine + Environment.NewLine +
            "Log: " + _log?.FilePath,
            "Petty",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        FinishAndShutdown();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _log?.Error("Unhandled exception.", e.ExceptionObject as Exception);
    }
}

