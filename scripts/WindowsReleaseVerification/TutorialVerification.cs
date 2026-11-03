using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Petty.App;
using Petty.App.Views;
using Petty.Core.Domain;
using Petty.Core.Persistence;
using Petty.Core.Preferences;
using Petty.Core.Runtime;

internal static partial class Program
{
    private static int CheckTutorialScenarios()
    {
        string output = Path.GetFullPath(Path.Combine("artifacts", "verification", "tutorial", Guid.NewGuid().ToString("N")));
        foreach ((string scenario, string folder) in new[]
        {
            ("close", "closed"), ("close", "closed"), ("skip", "skipped"),
            ("resume", "skipped"), ("finish", "finished"), ("resume", "finished"),
            ("legacy", "legacy"), ("corrupt", "corrupt"), ("replay", "replay")
        })
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.Environment["PETTY_VERIFY_TUTORIAL_SCENARIO"] = scenario;
            start.Environment["PETTY_DATA_DIR"] = Path.Combine(output, folder);
            using Process process = Process.Start(start)!;
            if (!process.WaitForExit(15000))
            {
                process.Kill(entireProcessTree: true);
                throw new InvalidOperationException($"Tutorial scenario timed out: {scenario}.");
            }
            Require(process.ExitCode == 0, $"Tutorial scenario failed: {scenario} ({process.ExitCode}).");
        }
        Console.WriteLine("TUTORIAL VERIFIED: first close/retry, skip/finish before canceled creation, remembered completion, legacy preferences, corrupt save recovery and settings replay. Simulated window interactions only.");
        return 0;
    }

    private static int CheckTutorialStartup(string scenario)
    {
        string data = Environment.GetEnvironmentVariable("PETTY_DATA_DIR")!;
        Directory.CreateDirectory(data);
        string preferences = Path.Combine(data, "preferences.json");
        string state = Path.Combine(data, "pet-state.json");
        if (scenario == "legacy")
        {
            File.WriteAllText(preferences, "{\"language\":\"en\",\"autostart\":false}");
        }
        if (scenario == "corrupt")
        {
            File.WriteAllText(state, "invalid save");
        }
        if (scenario == "replay")
        {
            new PetStateStore(state).Save(PetRuntime.CreateNew("TutorialCheck", PetTuning.Default, DateTimeOffset.UtcNow).ToSnapshot());
        }
        var app = new App();
        app.InitializeComponent();
        bool sawTutorial = false;
        bool sawCreator = false;
        bool replayRequested = false;
        object? session = null;
        Exception? failure = null;
        int nextClicks = 0;
        double? footerTop = null;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        var elapsed = Stopwatch.StartNew();
        app.Startup += (_, _) => timer.Start();
        timer.Tick += (_, _) =>
        {
            try
            {
                Require(elapsed.Elapsed < TimeSpan.FromSeconds(10), "Tutorial startup stalled.");
                TutorialWindow? tutorial = app.Windows.OfType<TutorialWindow>().FirstOrDefault();
                if (tutorial is not null)
                {
                    sawTutorial = true;
                    var frame = (Frame)tutorial.FindName("PageFrame");
                    Require(frame.Content is Page { IsLoaded: true }, "Tutorial frame did not load its own Page.");
                    var navigation = (Grid)tutorial.FindName("NavigationPanel");
                    double top = navigation.TranslatePoint(new Point(), (UIElement)tutorial.Content).Y;
                    footerTop ??= top;
                    Require(Math.Abs(top - footerTop.Value) < 0.1, "Loaded tutorial footer moved between pages.");
                    if (scenario == "finish")
                    {
                        string[] pageTypes = ["TutorialWelcomePage", "TutorialFeedingPage", "TutorialInteractionPage", "TutorialTrayPage"];
                        Require(frame.Content.GetType().Name == pageTypes[nextClicks], "Tutorial did not navigate to the expected Page.");
                        Require(((Button)tutorial.FindName("SkipButton")).Visibility == (nextClicks == 3 ? Visibility.Collapsed : Visibility.Visible),
                            "Loaded tutorial has incorrect Skip visibility.");
                    }
                    if (scenario is "close" or "replay")
                    {
                        tutorial.Close();
                    }
                    else if (scenario == "skip")
                    {
                        ((Button)tutorial.FindName("SkipButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    else
                    {
                        Require(scenario == "finish", "Existing user unexpectedly saw onboarding.");
                        ((Button)tutorial.FindName("NextButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        nextClicks++;
                    }
                    return;
                }
                CreationWizardWindow? creator = app.Windows.OfType<CreationWizardWindow>().FirstOrDefault();
                if (creator is not null)
                {
                    sawCreator = true;
                    creator.Close();
                    return;
                }
                if (scenario == "replay" && Get(app, "_session") is { } liveSession)
                {
                    if (!replayRequested)
                    {
                        Require(!sawTutorial, "Existing pet unexpectedly saw onboarding.");
                        session = liveSession;
                        Invoke(app, "ShowSettings");
                        var settings = (SettingsWindow)Get(app, "_settings")!;
                        replayRequested = true;
                        app.Dispatcher.BeginInvoke(() => ((Button)settings.FindName("TutorialButton"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                    }
                    else
                    {
                        Require(sawTutorial && ReferenceEquals(session, liveSession), "Replay replaced or stopped the pet session.");
                        Require(Get(app, "_settings") is SettingsWindow { IsVisible: true }, "Replay closed Settings.");
                        Invoke(app, "FinishAndShutdown");
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                app.Shutdown(1);
            }
        };
        int result = app.Run();
        timer.Stop();
        if (failure is not null)
        {
            throw new InvalidOperationException($"Tutorial scenario {scenario} failed.", failure);
        }
        Require(result == 0, "App did not exit successfully.");
        if (scenario == "close")
        {
            Require(sawTutorial && !sawCreator && !File.Exists(preferences) && !File.Exists(state),
                "Closing onboarding must leave it incomplete and must not create a pet.");
        }
        else if (scenario == "replay")
        {
            Require(sawTutorial && !sawCreator, "Replay opened pet creation.");
        }
        else
        {
            Require(sawCreator, "Creator did not follow onboarding or existing-user startup.");
            Require(sawTutorial == (scenario is "skip" or "finish"), "Incorrect onboarding eligibility.");
            if (scenario is "skip" or "finish" or "resume")
            {
                Require(new PreferencesStore(preferences).Load().TutorialCompleted && !File.Exists(state),
                    "Tutorial completion must persist even if creation is canceled.");
            }
            if (scenario == "finish")
            {
                Require(nextClicks == 4, "Tutorial did not have exactly four pages.");
            }
        }
        Console.WriteLine($"Tutorial scenario passed: {scenario}.");
        return 0;
    }
}
