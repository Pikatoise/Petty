using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Petty.App;
using Petty.Core.Behavior;
using Petty.Core.Domain;
using Petty.Core.Persistence;
using Petty.Core.Runtime;

internal static partial class Program
{
    // Release-only desktop measurements. Reflection keeps diagnostic controls out of the app.
    private static int Profile()
    {
        Require(typeof(App).Assembly.GetType("Petty.App.Views.DebugWindow") is null, "Use Release for profiling.");
        string output = Path.GetFullPath(Path.Combine("artifacts", "verification", "performance", Guid.NewGuid().ToString("N")));
        string data = Path.Combine(output, "data");
        Directory.CreateDirectory(data);
        new PetStateStore(Path.Combine(data, "pet-state.json")).Save(
            PetRuntime.CreateNew("Profile", PetTuning.Default, DateTimeOffset.UtcNow).ToSnapshot());
        Environment.SetEnvironmentVariable("PETTY_DATA_DIR", data);
        var app = new App();
        app.InitializeComponent();
        using var process = Process.GetCurrentProcess();
        PetActivity[] states = [PetActivity.Idle, PetActivity.Walking, PetActivity.Sleeping, PetActivity.Playing];
        var results = new List<object>();
        var probes = new List<CallbackProbe>();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        var clock = Stopwatch.StartNew();
        TimeSpan phaseStarted = TimeSpan.Zero;
        TimeSpan cpuStarted = TimeSpan.Zero;
        TimeSpan measuredAt = TimeSpan.Zero;
        long allocatedAt = 0;
        long minWorkingSet = long.MaxValue;
        long maxWorkingSet = 0;
        int phase = -1;
        int samples = 0;
        int matching = 0;
        bool measuring = false;
        Exception? failure = null;
        app.Startup += (_, _) => timer.Start();
        timer.Tick += (_, _) =>
        {
            try
            {
                if (clock.Elapsed < TimeSpan.FromSeconds(3))
                {
                    return;
                }
                var runtime = (PetRuntime)Get(app, "_runtime")!;
                if (phase < 0)
                {
                    object session = Get(app, "_session")!;
                    foreach ((string field, string method) in new[]
                    {
                        ("_frameTimer", "OnFrameTick"), ("_simulationTimer", "OnSimulationTick"),
                        ("_hoverTimer", "OnPointerChanged"), ("_shellBarTimer", "OnShellBarTick"),
                    })
                    {
                        probes.Add(new CallbackProbe(session, field, method));
                    }
                    // Prevent wandering during idle; keep normal play duration and motion speed.
                    typeof(PetBehaviorMachine).GetField("_tuning", PrivateInstance)!.SetValue(runtime.Behavior,
                        new PetTuning { WanderMinInterval = TimeSpan.FromHours(1), WanderMaxInterval = TimeSpan.FromHours(1) });
                    phase = 0;
                    BeginPhase();
                }
                if (!measuring && clock.Elapsed - phaseStarted >= TimeSpan.FromSeconds(3))
                {
                    measuring = true;
                    measuredAt = clock.Elapsed;
                    cpuStarted = process.TotalProcessorTime;
                    allocatedAt = GC.GetTotalAllocatedBytes();
                    foreach (CallbackProbe probe in probes)
                    {
                        probe.Reset();
                    }
                }
                if (measuring)
                {
                    process.Refresh();
                    minWorkingSet = Math.Min(minWorkingSet, process.WorkingSet64);
                    maxWorkingSet = Math.Max(maxWorkingSet, process.WorkingSet64);
                    samples++;
                    if (runtime.Behavior.State == states[phase])
                    {
                        matching++;
                    }
                }
                // Repeat normal walks and six-second play scenes, counting transition samples too.
                if (states[phase] is PetActivity.Walking or PetActivity.Playing && runtime.Behavior.State is PetActivity.Idle)
                {
                    Invoke(runtime.Behavior, "MoveTo", states[phase], "profile-repeat");
                }
                if (!measuring || clock.Elapsed - measuredAt < TimeSpan.FromSeconds(20))
                {
                    return;
                }
                double seconds = (clock.Elapsed - measuredAt).TotalSeconds;
                var result = new
                {
                    State = states[phase].ToString(),
                    Seconds = seconds,
                    StateCoverage = matching / (double)samples,
                    CpuPercentOfOneCore = (process.TotalProcessorTime - cpuStarted).TotalSeconds / seconds * 100,
                    AllocatedBytesPerSecond = (GC.GetTotalAllocatedBytes() - allocatedAt) / seconds,
                    MinWorkingSetMiB = minWorkingSet / 1048576.0,
                    MaxWorkingSetMiB = maxWorkingSet / 1048576.0,
                    PrivateMiB = process.PrivateMemorySize64 / 1048576.0,
                    Callbacks = probes.Select(p => new { p.Name, p.Calls, Milliseconds = p.Elapsed.TotalMilliseconds, p.AllocatedBytes }).ToArray(),
                };
                results.Add(result);
                Console.WriteLine(JsonSerializer.Serialize(result));
                if (++phase == states.Length)
                {
                    File.WriteAllText(Path.Combine(output, "profile.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
                    timer.Stop();
                    Invoke(app, "FinishAndShutdown");
                    return;
                }
                BeginPhase();

                void BeginPhase()
                {
                    runtime.Behavior.StartIdle();
                    if (states[phase] != PetActivity.Idle)
                    {
                        Invoke(runtime.Behavior, "MoveTo", states[phase], "profile");
                    }
                    phaseStarted = clock.Elapsed;
                    measuring = false;
                    samples = matching = 0;
                    minWorkingSet = long.MaxValue;
                    maxWorkingSet = 0;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
                timer.Stop();
                app.Shutdown(1);
            }
        };
        int exitCode = app.Run();
        if (failure is not null)
        {
            throw new InvalidOperationException("Profile failed.", failure);
        }
        Require(results.Count == states.Length, "Profile did not complete; check the single-instance guard.");
        return exitCode;
    }

    private sealed class CallbackProbe
    {
        public string Name
        {
            get;
        }
        public int Calls
        {
            get; private set;
        }
        public TimeSpan Elapsed
        {
            get; private set;
        }
        public long AllocatedBytes
        {
            get; private set;
        }

        public CallbackProbe(object session, string field, string method)
        {
            Name = method;
            var timer = (DispatcherTimer)Get(session, field)!;
            var callback = (EventHandler)Delegate.CreateDelegate(typeof(EventHandler), session, method);
            timer.Tick -= callback;
            timer.Tick += (sender, e) =>
            {
                long allocated = GC.GetAllocatedBytesForCurrentThread();
                long started = Stopwatch.GetTimestamp();
                try
                {
                    callback(sender, e);
                }
                finally
                {
                    Calls++;
                    Elapsed += Stopwatch.GetElapsedTime(started);
                    AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - allocated;
                }
            };
        }

        public void Reset()
        {
            Calls = 0;
            Elapsed = TimeSpan.Zero;
            AllocatedBytes = 0;
        }
    }
}
