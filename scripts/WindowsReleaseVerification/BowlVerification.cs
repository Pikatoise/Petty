using System.Runtime.InteropServices;
using Petty.App;
using Petty.App.Views;
using Petty.Core.Behavior;
using Petty.Core.Domain;
using Petty.Core.Runtime;

internal static partial class Program
{
    private static void CheckBowlDragging(App app)
    {
        object session = Get(app, "_session")!;
        var runtime = (PetRuntime)Get(app, "_runtime")!;
        var bowl = (PropWindow)Get(app, "_bowlWindow")!;
        runtime.Behavior.StartIdle();
        runtime.RefillBowl(100);
        (int Left, int Top, int Width, int Height) original = bowl.ScreenRect;

        // Simulate a held press without moving the owner's pointer or injecting global input.
        HoldBowlAndMove(bowl);
        Require(bowl.IsDragging && bowl.ScreenRect.Top < original.Top, "Unlocked bowl did not drag.");
        runtime.Behavior.Update(TimeSpan.FromMilliseconds(1), Needs.FromValues(10, 100), runtime.Bowl);
        Require(runtime.Behavior.State is PetActivity.SeekingFood, "Hungry pet did not seek its bowl.");
        runtime.NotifyArrived();
        Require(runtime.Behavior.State is PetActivity.Eating && !bowl.IsDragging,
            "Starting a meal did not cancel an active bowl drag.");
        Require(bowl.ScreenRect == original, "Starting a meal did not restore the feeding position.");

        int clicks = 0;
        EventHandler clicked = (_, _) => clicks++;
        bowl.Clicked += clicked;
        try
        {
            HoldBowlAndMove(bowl);
            SendMessage(bowl.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero); // WM_LBUTTONUP
            Require(!bowl.IsDragging && bowl.ScreenRect == original, "Bowl moved while the pet was eating.");
            Require(clicks == 0, "A rejected drag refilled the bowl.");
        }
        finally
        {
            bowl.Clicked -= clicked;
            runtime.Behavior.StartIdle();
        }

        HoldBowlAndMove(bowl);
        Require(bowl.IsDragging, "Bowl remained locked after eating.");
        SendMessage(bowl.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
        object props = Get(session, "_props")!;
        props.GetType().GetMethod("AdvanceBowlDrop")!.Invoke(props, [TimeSpan.FromSeconds(1), PetTuning.Default.BowlDropDuration]);
        Require(bowl.ScreenRect.Top == original.Top, "Released bowl did not land on the shared floor.");

        // A display can shrink while held, leaving the window outside the new drag bounds.
        HoldBowlAndMove(bowl);
        ScreenRect bounds = bowl.DragBounds!.Value;
        bowl.PlaceAt(bounds.Right + 100, original.Top - 100);
        props.GetType().GetMethod("ApplyBowlPosition")!.Invoke(props, [runtime.Snapshot.Placement]);
        int heldTop = bowl.ScreenRect.Top;
        SendMessage(bowl.Handle, 0x0202, IntPtr.Zero, IntPtr.Zero);
        Require(bowl.ScreenRect.Left >= bounds.X && bowl.ScreenRect.Left + bowl.PhysicalWidth <= bounds.Right,
            "Releasing a bowl after its drag bounds shrink left it off screen.");
        Require(bowl.ScreenRect.Top == heldTop, "Recovering a released bowl skipped its falling animation.");
        props.GetType().GetMethod("AdvanceBowlDrop")!.Invoke(props, [TimeSpan.FromSeconds(1), PetTuning.Default.BowlDropDuration]);
        Require(bowl.ScreenRect.Top == original.Top, "Recovered bowl did not land on the shared floor.");
    }

    private static void HoldBowlAndMove(PropWindow bowl)
    {
        Require(GetCursorPos(out BowlCursor point), "Cannot read the pointer position.");
        foreach ((string field, object value) in new (string, object)[]
        {
            ("_pressed", true), ("_dragging", false),
            ("_pressCursorX", point.X), ("_pressCursorY", point.Y + 20),
            ("_pressLeft", bowl.ScreenRect.Left), ("_pressTop", bowl.ScreenRect.Top),
        })
        {
            typeof(PropWindow).GetField(field, PrivateInstance)!.SetValue(bowl, value);
        }

        SendMessage(bowl.Handle, 0x0200, IntPtr.Zero, IntPtr.Zero); // WM_MOUSEMOVE
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BowlCursor
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out BowlCursor point);
}
