using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Petty.App;
using Petty.App.Desktop;
using Petty.App.Views;

internal static partial class Program
{
    private static void CheckDesktopRecovery(App app)
    {
        var pet = (PetWindow)Get(app, "_window")!;
        var bowl = (PropWindow)Get(app, "_bowlWindow")!;
        var ball = (PropWindow)Get(app, "_ballWindow")!;
        object watcher = Get(app, "_shellWatcher")!;
        MethodInfo covered = typeof(DesktopWindowService).GetMethod("IsCoveredByDesktop", BindingFlags.Static | BindingFlags.NonPublic)!;
        nint desktop = FindDesktopWindow("Progman", null);
        Require(desktop != IntPtr.Zero, "Desktop shell is unavailable for z-order verification.");
        Require((bool)covered.Invoke(null, [pet.Handle, desktop])! == false, "Pet did not start above the desktop.");

        // A synthetic desktop reproduces the native ordering without changing the owner's shell.
        // Windows otherwise forces the real Progman to remain at the bottom of the z-order.
        using var raisedDesktop = new HwndSource(new HwndSourceParameters("Petty.VerificationDesktop")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
        });
        Require(SetDesktopWindowPos(raisedDesktop.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x13), "Could not raise synthetic desktop.");
        Require((bool)covered.Invoke(null, [pet.Handle, raisedDesktop.Handle])!, "Regression setup did not cover the pet.");
        string logPath = Path.Combine(Environment.GetEnvironmentVariable("PETTY_DATA_DIR")!, "petty.log");
        int before = File.ReadAllText(logPath).Split("Shell recovery: desktop windows reattached").Length;
        Invoke(watcher, "OnDesktopPoll", null, EventArgs.Empty);
        Require(File.ReadAllText(logPath).Split("Shell recovery: desktop windows reattached").Length == before,
            "An ordinary window above the pet incorrectly triggered desktop recovery.");
        watcher.GetType().GetMethod("RestoreDesktopOrder", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(watcher, [raisedDesktop.Handle]);
        Require(File.ReadAllText(logPath).Split("Shell recovery: desktop windows reattached").Length == before + 1,
            "Covered desktop did not trigger automatic recovery.");
        Require((bool)covered.Invoke(null, [pet.Handle, desktop])! == false, "Desktop recovery did not reattach the pet.");
        Require((bool)covered.Invoke(null, [bowl.Handle, desktop])! == false, "Desktop recovery did not reattach the bowl.");
        Require(!pet.Topmost && !bowl.Topmost && !ball.IsVisible, "Recovery changed desktop flags or showed a hidden prop.");
        Require(((DispatcherTimer)Get(watcher, "_desktopTimer")!).IsEnabled, "Desktop recovery timer is stopped.");
    }

    [DllImport("user32.dll", EntryPoint = "FindWindowW", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindDesktopWindow(string className, string? title);

    [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDesktopWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
}
