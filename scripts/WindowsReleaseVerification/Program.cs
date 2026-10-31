using Microsoft.Win32;
using Petty.App;
using Petty.App.Views;
using Petty.Core.Domain;
using Petty.Core.Persistence;
using Petty.Core.Preferences;
using Petty.Core.Runtime;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

internal static class Program
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type Autostart = typeof(App).Assembly.GetType("Petty.App.Desktop.AutostartService", throwOnError: true)!;

    [STAThread]
    private static int Main()
    {
        var output = Path.GetFullPath(Path.Combine("artifacts", "session11", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(output);
        CheckAutostart(output);
        Require(typeof(App).Assembly.GetType("Petty.App.Views.DebugWindow") is null, "Use Release: developer UI must be absent.");
        var data = Path.Combine(output, "isolated data");
        var store = new PetStateStore(Path.Combine(data, "pet-state.json"), PetTuning.Default);
        var missingAsset = Environment.GetEnvironmentVariable("PETTY_VERIFY_MISSING_ASSET") == "1";
        var snapshot = PetRuntime.CreateNew("WindowsCheck", PetTuning.Default, DateTimeOffset.UtcNow).ToSnapshot();
        if (missingAsset) snapshot.AssetId = "missing-verification-asset";
        store.Save(snapshot);
        var originalState = File.ReadAllText(Path.Combine(data, "pet-state.json"));
        Environment.SetEnvironmentVariable("PETTY_DATA_DIR", data);
        var sessionEnding = Environment.GetEnvironmentVariable("PETTY_VERIFY_SESSION_ENDING") == "1";
        var app = new App();
        app.InitializeComponent();
        var phase = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        Exception? failure = null;
        var started = Stopwatch.StartNew();
        var endingAt = TimeSpan.Zero;
        app.Startup += (_, _) => timer.Start();
        timer.Tick += (_, _) =>
        {
            try
            {
                if (missingAsset)
                {
                    var dismissed = false;
                    EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
                    {
                        var name = new StringBuilder(64);
                        GetClassName(hwnd, name, name.Capacity);
                        if (name.ToString() != "#32770") return true;
                        dismissed = true;
                        SendMessage(hwnd, 0x111, new IntPtr(7), IntPtr.Zero); // WM_COMMAND / IDNO
                        return false;
                    }, IntPtr.Zero);
                    Require(dismissed, "Missing-asset recovery prompt was not shown.");
                    timer.Stop();
                    return;
                }
                if (phase++ == 0)
                {
                    Require(Get(app, "_session") is not null, "Pet session did not start.");
                    using (var second = Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "Petty.exe")) { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })!)
                        Require(second.WaitForExit(3000) && second.ExitCode == 3, "Second instance did not exit promptly with code 3.");
                    Invoke(app, "ShowSettings");
                    var settings = (SettingsWindow)Get(app, "_settings")!;
                    Invoke(settings, "ShowAutostartError");
                    Require(((System.Windows.Controls.TextBlock)settings.FindName("StatusText")).Text.Length > 0,
                        "Silent autostart error feedback is missing.");
                    settings.Close();
                    var tray = Get(app, "_tray")!;
                    Invoke(tray, "OnMouseUp", null, new Forms.MouseEventArgs(Forms.MouseButtons.Right, 1, 0, 0, 0));
                    Require(Get(tray, "_popup") is not null, "Tray popup did not open.");
                    var watcher = Get(app, "_shellWatcher")!;
                    var source = (HwndSource)Get(watcher, "_source")!;
                    SendMessage(source.Handle, RegisterWindowMessage("TaskbarCreated"), IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                timer.Stop();
                var service = Get(app, "_tray")!;
                Require(Get(service, "_popup") is null, "Shell recovery left a stale tray popup.");
                Require(File.ReadAllText(Path.Combine(data, "petty.log")).Contains("desktop windows reattached"), "Shell notification was not processed.");
                Invoke(service, "OnMouseUp", null, new Forms.MouseEventArgs(Forms.MouseButtons.Right, 1, 0, 0, 0));
                Require(Get(service, "_popup") is not null, "Tray popup could not reopen after recovery.");
                service.GetType().GetMethod("ClosePopup")!.Invoke(service, null);
                foreach (var pet in app.Windows.OfType<PetWindow>())
                    Require(!pet.Topmost && !pet.ShowInTaskbar, "Desktop window flags changed.");
                endingAt = started.Elapsed;
                if (sessionEnding)
                {
                    var e = (SessionEndingCancelEventArgs)Activator.CreateInstance(typeof(SessionEndingCancelEventArgs),
                        BindingFlags.Instance | BindingFlags.NonPublic, null, [ReasonSessionEnding.Logoff], null)!;
                    Invoke(app, "OnSessionEnding", e);
                    Require(!e.Cancel, "Session ending was cancelled.");
                    // WPF normally follows the non-cancelled event with Shutdown.
                    app.Shutdown();
                }
                else Invoke(app, "RequestExitFromTray");
            }
            catch (Exception ex)
            {
                failure = ex;
                timer.Stop();
                app.Shutdown(1);
            }
        };
        var result = app.Run();
        if (failure is not null) throw new InvalidOperationException("Windows lifecycle verification failed.", failure);
        Require(result == 0, "App returned a failure code.");
        if (missingAsset)
        {
            Require(File.ReadAllText(Path.Combine(data, "pet-state.json")) == originalState, "Recovery decline changed the existing save.");
            Require(!File.ReadAllText(Path.Combine(data, "petty.log")).Contains("Autostart synchronized"), "Failed startup registered autostart.");
            Console.WriteLine($"PASS: missing appearance recovery declined; save untouched. {output}");
            return 0;
        }
        Require(store.Load().Snapshot?.PetName == "WindowsCheck", "Saved pet did not reload.");
        var log = File.ReadAllText(Path.Combine(data, "petty.log"));
        Require(log.Contains("Autostart integration skipped"), "Isolated startup touched registry integration.");
        Require(log.Contains(sessionEnding ? "Pet state saved (session-ending)" : "Pet state saved (shutdown)"), "Final save missing.");
        Require(sessionEnding ? !log.Contains("-> Exiting") : log.Contains("-> Exiting"), "Wrong departure policy.");
        if (sessionEnding) Require(started.Elapsed - endingAt < TimeSpan.FromSeconds(2), "Session ending waited too long.");
        Console.WriteLine($"PASS: isolated registry, shell broadcast, tray reopen, {(sessionEnding ? "fast session ending" : "animated exit")}, saved state. {output}");
        return 0;
    }

    private static void CheckAutostart(string output)
    {
        var path = @"Software\PettyVerification\" + Guid.NewGuid().ToString("N");
        var first = Path.Combine(output, "folder with spaces", "Petty.exe");
        var moved = Path.Combine(output, "moved folder", "Petty.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(moved)!);
        File.WriteAllText(first, "test executable path");
        File.WriteAllText(moved, "test executable path");
        var apply = Autostart.GetMethod("Apply", BindingFlags.Static | BindingFlags.NonPublic)!;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path);
            apply.Invoke(null, [key, true, first]);
            Require(key.GetValue("Petty") as string == $"\"{first}\"", "Fresh autostart command is not quoted.");
            using (var readOnly = Registry.CurrentUser.OpenSubKey(path, writable: false)!)
            {
                apply.Invoke(null, [readOnly, true, first]); // Must not write an unchanged value.
                ExpectFailure(() => apply.Invoke(null, [readOnly, true, moved]), typeof(UnauthorizedAccessException));
            }
            apply.Invoke(null, [key, true, moved]);
            Require(key.GetValue("Petty") as string == $"\"{moved}\"", "Moved executable was not repaired.");
            key.DeleteValue("Petty");
            apply.Invoke(null, [key, true, moved]);
            Require(key.GetValue("Petty") is not null, "Missing startup value was not repaired.");
            var preferences = new PreferencesStore(Path.Combine(output, "preferences.json"));
            preferences.Save(new UserPreferences(Autostart: false));
            apply.Invoke(null, [key, preferences.Load().Autostart, moved]);
            apply.Invoke(null, [key, preferences.Load().Autostart, moved]);
            Require(key.GetValue("Petty") is null, "Disabled autostart was re-enabled.");
            ExpectFailure(() => apply.Invoke(null, [key, true, "relative.exe"]), typeof(IOException));
            Require(key.GetValue("Petty") is null, "Invalid path mutated startup registration.");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false); }
    }

    private static object? Get(object target, string field) => target.GetType().GetField(field, PrivateInstance)!.GetValue(target);
    private static object? Invoke(object target, string method, params object?[] args) => target.GetType().GetMethod(method, PrivateInstance)!.Invoke(target, args);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void ExpectFailure(Action action, Type expected)
    {
        try { action(); }
        catch (TargetInvocationException ex) when (ex.InnerException?.GetType() == expected) { return; }
        throw new InvalidOperationException($"Expected {expected.Name}.");
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);
    private delegate bool EnumThreadWindowProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint threadId, EnumThreadWindowProc callback, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int maxCount);
}
