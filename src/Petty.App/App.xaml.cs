using Petty.App.Desktop;
using Petty.App.Pet;
using Petty.App.Sprites;
using Petty.App.Tray;
using Petty.App.Views;
using Petty.Core.Diagnostics;
using Petty.Core.Domain;
using Petty.Core.Persistence;
using Petty.Core.Runtime;
using Petty.Core.Preferences;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace Petty.App;

/// <summary>
/// Application composition root. Stage 3 starts the pet directly with a provisionally
/// loaded pet; stage 4 adds the tray icon, the first-run wizard and the settings
/// window around this same session.
/// </summary>
public partial class App: Application
{
    private sealed class StartupMessageException(string message) : Exception(message) { }
    private static readonly JsonSerializerOptions ReportJsonOptions = new() { WriteIndented = true };

    private readonly DisplayService _displayService = new();

    private AppOptions _options = new();
    private PetPaths? _paths;
    private string? _assetDirectory;
    private IReadOnlyList<SpriteVariant> _variants = Array.Empty<SpriteVariant>();
    private IReadOnlyList<DisplayInfo> _displayLayout = Array.Empty<DisplayInfo>();
    private FileAppLog? _log;
    private PetStateStore? _store;
    private PetTuning? _tuning;
    private PetRuntime? _runtime;
    private SpriteSheet? _sheet;
    private PetWindow? _window;
    private PropWindow? _bowlWindow;
    private PropWindow? _ballWindow;
    private PropImage? _bowlEmptyImage;
    private PropImage? _bowlFullImage;
    private PetSession? _session;
    private TrayService? _tray;
    private SettingsWindow? _settings;
    private DispatcherTimer? _exitTimer;
    private DispatcherTimer? _autoSaveTimer;
    private System.Threading.Mutex? _singleInstance;
    private bool _stateWasPersisted;
    private bool _shutdownStarted;
    private Localization? _localization;

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
            var preferences = new PreferencesStore(Path.Combine(_paths.DataDirectory, "preferences.json"));
            _localization = new Localization(preferences, preferences.Load(message => _log.Warn(message)));
            _localization.LanguageChanged += OnLanguageChanged;
        }
        catch (Exception ex)
        {
            _log?.Error("Startup initialization failed.", ex);
            var initialMessage = System.Globalization.CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ru"
                ? "Не удалось запустить Petty. Проверьте параметры запуска и доступ к папке данных."
                : "Petty could not start. Check its launch arguments and access to the data folder.";
            MessageBox.Show(
                initialMessage + Environment.NewLine + Environment.NewLine + ex.Message,
                "Petty",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        _log.Info($"Petty starting (pid {Environment.ProcessId}). Data directory: {_paths.DataDirectory}");

        // Two instances would fight over the same state file and tray icon.
        _singleInstance = new System.Threading.Mutex(initiallyOwned: true, @"Local\Petty.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            _log.Warn("Another Petty instance is already running; this one exits.");
            Shutdown(3);
            return;
        }

        try
        {
            StartPet();
        }
        catch (Exception ex)
        {
            _log.Error("Startup failed.", ex);
            MessageBox.Show(
                (ex is StartupMessageException ? ex.Message : Localization.Text("StartError")) + Environment.NewLine + Environment.NewLine +
                Localization.Text("LogPath") + Environment.NewLine + _log.FilePath,
                "Petty",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _exitTimer?.Stop();
        _autoSaveTimer?.Stop();
        _settings?.Close();
        _settings = null;
        _session?.Dispose();
        _session = null;
        _window = null;
        _sheet = null;
        _runtime = null;
        _bowlWindow = null;
        _ballWindow = null;
        _bowlEmptyImage = null;
        _bowlFullImage = null;
        _tray?.Dispose();
        _tray = null;
        if (_localization is not null) _localization.LanguageChanged -= OnLanguageChanged;
        _singleInstance?.Dispose();
        _singleInstance = null;
        _log?.Info("Petty stopped.");
        base.OnExit(e);
    }

    private void StartPet()
    {
        var now = DateTimeOffset.UtcNow;
        var tuning = PetTuning.Default.Sanitized();
        _tuning = tuning;

        _store = new PetStateStore(_paths!.StateFile, tuning);
        var load = _store.Load();
        _log!.Info($"State: {load.Status}. {load.Message}".TrimEnd());

        if (load.Status is LoadStatus.UnsupportedVersion)
            throw new StartupMessageException(Localization.Text("StateUnsupported"));

        var assetDirectory = SpriteAssetLocator.FindDirectory(PetCatalog.DefaultAssetId)
            ?? throw new StartupMessageException(Localization.Text("AssetsMissing"));

        _assetDirectory = assetDirectory;
        _variants = SpriteAssetLocator.Discover(Path.Combine(_paths.DataDirectory, "sprites"), message => _log.Warn(message));

        _displayLayout = _displayService.GetDisplays();

        PetRuntime runtime;

        if (load.Snapshot is not null)
        {
            var variant = _variants.FirstOrDefault(v => v.AssetId == load.Snapshot.AssetId)
                ?? throw new StartupMessageException(string.Format(Localization.Text("SavedAppearanceMissing"), load.Snapshot.AssetId));
            _sheet = SpriteSheetLoader.Load(variant.Directory, variant.AssetId);
            _stateWasPersisted = true;
            runtime = PetRuntime.Restore(load.Snapshot, tuning, now);
            _log.Info($"Restored '{runtime.Snapshot.PetName}' (satiety {runtime.Needs.Satiety:F0}, happiness {runtime.Needs.Happiness:F0}).");
        }
        else if (_options.CreateName is { } createName)
        {
            var variant = _variants.FirstOrDefault(v => v.AssetId == PetCatalog.DefaultAssetId)
                ?? _variants.FirstOrDefault() ?? throw new StartupMessageException(Localization.Text("NoSprites"));
            _sheet = SpriteSheetLoader.Load(variant.Directory, variant.AssetId);
            runtime = CreatePet(createName, tuning, variant);
            _log.Info($"Created '{runtime.Snapshot.PetName}' from --create-name.");
        }
        else
        {
            var warning = load.Status is LoadStatus.Corrupt or LoadStatus.UnsupportedVersion
                ? Localization.Text("CorruptState")
                : null;

            var wizard = new CreationWizardWindow(_variants, firstRun: true, PetNameRules.DefaultName, warning);
            if (wizard.ShowDialog() != true)
            {
                _log.Info("Pet creation was cancelled; exiting.");
                Shutdown();
                return;
            }

            _sheet = wizard.SelectedSheet!;
            runtime = CreatePet(wizard.ConfirmedName, tuning, wizard.SelectedVariant!);
        }

        _log.Info($"Loaded '{_sheet.Metadata.Character}': {_sheet.Clips.Count} clips, " +
                  $"{_sheet.FrameWidth}x{_sheet.FrameHeight} frames, ground row {_sheet.GroundRow}.");

        StartSession(runtime, tuning);

        // WPF finishes applying the window styles after the first frame, so the style
        // audit waits until the dispatcher is idle instead of reporting a false alarm.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            () => DesktopWindowService.VerifyPetWindowStyles(_window!.Handle, _log!));

        WriteReport(_paths.StartupReportFile, "startup");

        if (_options.ExitAfter is { } delay)
        {
            _log.Info($"Automatic exit scheduled in {delay.TotalSeconds:F1}s (diagnostic switch).");
            _exitTimer = new DispatcherTimer { Interval = delay };
            _exitTimer.Tick += OnExitTimerTick;
            _exitTimer.Start();
        }
    }

    /// <summary>Creates and immediately persists a pet, so autosave has a file to update.</summary>
    private PetRuntime CreatePet(string name, PetTuning tuning, SpriteVariant variant)
    {
        var runtime = PetRuntime.CreateNew(name, tuning, DateTimeOffset.UtcNow);
        runtime.Snapshot.AssetId = variant.AssetId;
        runtime.Snapshot.BreedId = variant.BreedId;
        runtime.Snapshot.CoatId = variant.CoatId;
        _stateWasPersisted = true;

        try
        {
            _store!.Save(runtime.ToSnapshot());
            _log?.Info($"New pet '{runtime.Snapshot.PetName}' saved.");
        }
        catch (Exception ex)
        {
            _log?.Error("Could not save the new pet; autosave will retry.", ex);
        }

        return runtime;
    }

    /// <summary>Shows the pet window (once) and runs a session for <paramref name="runtime"/>.</summary>
    private void StartSession(PetRuntime runtime, PetTuning tuning)
    {
        _runtime = runtime;

        if (_window is null)
        {
            _window = new PetWindow(_sheet!, _log!);
            _window.Show();
            _window.AttachToDesktop(_options.DesktopLayer, _log!);
        }

        if (_bowlWindow is null) CreatePropWindows();

        _session?.Dispose();
        _session = new PetSession(
            runtime, _sheet!, _window, _bowlWindow!, _ballWindow!, _displayService, tuning, _log!);
        _session.ExitAnimationCompleted += OnExitAnimationCompleted;
        _session.StateChanged += (_, reason) => SaveState(reason);
        _session.Start();

        if (_autoSaveTimer is null)
        {
            _autoSaveTimer = new DispatcherTimer { Interval = tuning.AutoSaveInterval };
            _autoSaveTimer.Tick += OnAutoSaveTick;
            _autoSaveTimer.Start();
        }

        if (_tray is null) CreateTray();
    }

    /// <summary>
    /// Bowl and ball live in their own layered windows, created with the first pet.
    /// The bowl is interactive (click = food, drag = move); the ball never takes input.
    /// </summary>
    private void CreatePropWindows()
    {
        _bowlEmptyImage = PropImage.Load(Path.Combine(_assetDirectory!, "bowl-empty.png"));
        _bowlFullImage = PropImage.Load(Path.Combine(_assetDirectory!, "bowl-full.png"));
        var ballImage = PropImage.Load(Path.Combine(_assetDirectory!, "ball.png"));

        _bowlWindow = new PropWindow(_bowlEmptyImage, _bowlFullImage, interactive: true, _log!);
        _bowlWindow.Show();
        _bowlWindow.AttachToDesktop(_options.DesktopLayer, _log!);

        _ballWindow = new PropWindow(ballImage, fullImage: null, interactive: false, _log!);
        _ballWindow.Show();
        _ballWindow.AttachToDesktop(_options.DesktopLayer, _log!);
        _ballWindow.Hide();

        _log!.Info(
            $"Prop windows ready (bowl {_bowlWindow.PhysicalWidth}x{_bowlWindow.PhysicalHeight}, " +
            $"ball {_ballWindow.PhysicalWidth}x{_ballWindow.PhysicalHeight}).");
    }

    private void CreateTray()
    {
        var iconPath = Path.Combine(_assetDirectory!, TrayService.TrayIconFileName);
        _tray = new TrayService(iconPath, _log!, Localization.Text("TraySettings"), Localization.Text("TrayExit"));
        _tray.SettingsRequested += (_, _) => ShowSettings();
        _tray.ExitRequested += (_, _) => RequestExitFromTray();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) =>
        _tray?.SetLanguage(Localization.Text("TraySettings"), Localization.Text("TrayExit"));

    internal void LogError(string message, Exception exception) => _log?.Error(message, exception);

    private void ShowSettings()
    {
        if (_runtime is null) return;

        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(_runtime.Snapshot.PetName, _runtime.Snapshot.BreedId, _localization!, _log!);
        _settings.RenameRequested += OnRenameRequested;
        _settings.NewPetRequested += OnNewPetRequested;
        _settings.Closed += (_, _) => _settings = null;
        _settings.Show();
        _settings.Activate();
    }

    private void OnRenameRequested(object? sender, string name)
    {
        if (_runtime is null) return;

        _runtime.Snapshot.PetName = name;
        _log?.Info($"Renamed the pet to '{name}'.");
        SaveState("rename");
    }

    private void OnNewPetRequested(object? sender, EventArgs e)
    {
        _settings?.Close();

        if (_sheet is null || _tuning is not { } tuning) return;

        _variants = SpriteAssetLocator.Discover(Path.Combine(_paths!.DataDirectory, "sprites"), message => _log!.Warn(message));
        var wizard = new CreationWizardWindow(_variants, firstRun: false, PetNameRules.DefaultName, warning: null, _sheet);
        if (wizard.ShowDialog() != true) return;

        if (!ReferenceEquals(_sheet, wizard.SelectedSheet))
        {
            _session?.Dispose();
            _session = null;
            _window?.Close();
            _window = null;
            _sheet = wizard.SelectedSheet;
        }
        var runtime = CreatePet(wizard.ConfirmedName, tuning, wizard.SelectedVariant!);
        StartSession(runtime, tuning);
        _log?.Info($"Recreated the pet as '{runtime.Snapshot.PetName}'.");
    }

    private void RequestExitFromTray()
    {
        _settings?.Close();

        if (_session is null || !_session.RequestExit())
            FinishAndShutdown();
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

    private void OnAutoSaveTick(object? sender, EventArgs e) => SaveState("autosave");

    private void SaveState(string reason)
    {
        if (!_stateWasPersisted || _runtime is null || _store is null) return;

        try
        {
            _store.Save(_runtime.ToSnapshot());
            _log?.Info($"Pet state saved ({reason}).");
        }
        catch (Exception ex)
        {
            _log?.Error($"Could not save pet state ({reason}).", ex);
        }
    }

    private void FinishAndShutdown()
    {
        if (_shutdownStarted) return;
        _shutdownStarted = true;

        _exitTimer?.Stop();
        _autoSaveTimer?.Stop();
        _log?.Info("Shutdown requested.");

        // Save first: a broken diagnostics report must never cost the pet's state.
        SaveState("shutdown");

        _settings?.Close();
        _settings = null;

        if (_paths is not null) WriteReport(_paths.FinalReportFile, "final");

        _session?.Dispose();
        _session = null;

        _window?.Close();
        _window = null;

        _bowlWindow?.Close();
        _bowlWindow = null;
        _ballWindow?.Close();
        _ballWindow = null;
        _bowlEmptyImage = null;
        _bowlFullImage = null;

        _tray?.Dispose();
        _tray = null;

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
                bowl = _bowlWindow is { } bowlWindow && _runtime is { } petRuntime
                    ? new
                    {
                        handle = $"0x{bowlWindow.Handle.ToInt64():X}",
                        rect = FormatRect(new ScreenRect(
                            bowlWindow.ScreenRect.Left,
                            bowlWindow.ScreenRect.Top,
                            bowlWindow.ScreenRect.Width,
                            bowlWindow.ScreenRect.Height)),
                        hitTests = DesktopWindowService.ProbeHitTest(bowlWindow.Handle, new[]
                        {
                            (bowlWindow.ScreenRect.Left + (bowlWindow.ScreenRect.Width / 2),
                             bowlWindow.ScreenRect.Top + (bowlWindow.ScreenRect.Height / 2)),
                            (bowlWindow.ScreenRect.Left + 1, bowlWindow.ScreenRect.Top + 1),
                        }),
                        food = Math.Round(petRuntime.Bowl.Food, 1),
                        filled = petRuntime.Bowl.HasFood,
                    }
                    : null,
                ball = _ballWindow is { } ballWindow && ballWindow.IsVisible
                    ? new
                    {
                        handle = $"0x{ballWindow.Handle.ToInt64():X}",
                        rect = FormatRect(new ScreenRect(
                            ballWindow.ScreenRect.Left,
                            ballWindow.ScreenRect.Top,
                            ballWindow.ScreenRect.Width,
                            ballWindow.ScreenRect.Height)),
                    }
                    : null,
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
        catch (Exception ex)
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
            Localization.Text("FatalError") + Environment.NewLine + Environment.NewLine +
            Localization.Text("LogPath") + Environment.NewLine + _log?.FilePath,
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

