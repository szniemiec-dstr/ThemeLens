// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using ThemeLens.Engine;
using ThemeLens.Platform.Windows;

namespace ThemeLens.Platform.Tests;

/// <summary>
/// The whole lens, started the way the app starts it, on whatever desktop the test machine has: windows,
/// composition, swap chain, frame loop and a frame source. Nothing here looks at the picture; the point is
/// that the parts start, reach "Live" and stop again without an error. They need a desktop session.
/// </summary>
[Trait("Category", "Ui")]
public sealed partial class LensRuntimeSmokeTests
{
    private static readonly TimeSpan StartUp = TimeSpan.FromSeconds(15);

    /// <summary>No screen capture involved: the lens shows the built-in test pattern.</summary>
    [WindowsFact]
    public void The_lens_goes_live_over_the_test_pattern_and_switches_off_again()
    {
        using var runtime = new LensRuntime(Options(FrameSourceKind.Pattern));
        runtime.Controller.State.ShouldBe(LensState.Off);

        runtime.ToggleLens();
        WaitFor(() => runtime.Controller.State == LensState.Live, StartUp).ShouldBeTrue(Describe(runtime, "did not go live"));

        // Stay live for a while: a fault in the frame loop shows up as a fall back to the background.
        Thread.Sleep(TimeSpan.FromSeconds(1.5));
        runtime.Controller.State.ShouldBe(LensState.Live, Describe(runtime, "did not stay live"));

        // The lens is on screen, and the pointer goes through it (FR-02): asked what is under the middle of
        // the lens, Windows names some other window, or none.
        nint lens = FindWindow("ThemeLens.Lens", null);
        lens.ShouldNotBe(0, "there is no lens window");
        IsWindowVisible(lens).ShouldBeTrue("the lens window is not shown");
        PixelRect bounds = runtime.LensBounds;
        var middle = new Point { X = bounds.X + (bounds.Width / 2), Y = bounds.Y + (bounds.Height / 2) };
        WindowFromPoint(middle).ShouldNotBe(lens, "the pointer must pass through the lens to whatever is underneath");

        // In edit mode the lens takes the pointer, so that it can be moved and resized (FR-03).
        runtime.SetEditMode(true);
        WaitFor(() => runtime.Controller.State == LensState.Edit, StartUp).ShouldBeTrue(Describe(runtime, "did not enter edit mode"));
        WaitFor(() => WindowFromPoint(middle) == lens, StartUp).ShouldBeTrue("in edit mode the lens must take the pointer");
        runtime.SetEditMode(false);
        WaitFor(() => runtime.Controller.State == LensState.Live, StartUp).ShouldBeTrue(Describe(runtime, "did not leave edit mode"));
        WaitFor(() => WindowFromPoint(middle) != lens, StartUp).ShouldBeTrue("after edit mode the pointer must pass through again");

        runtime.ToggleFillScreen();
        Thread.Sleep(TimeSpan.FromSeconds(0.5));
        runtime.Controller.State.ShouldBe(LensState.Live, Describe(runtime, "did not survive filling the screen"));
        runtime.ToggleFillScreen();
        Thread.Sleep(TimeSpan.FromSeconds(0.5));
        runtime.Controller.State.ShouldBe(LensState.Live, Describe(runtime, "did not survive going back to its size"));

        runtime.ToggleLens();
        WaitFor(() => runtime.Controller.State == LensState.Off, StartUp).ShouldBeTrue(Describe(runtime, "did not switch off"));
        WaitFor(() => !IsWindowVisible(lens), StartUp).ShouldBeTrue("the lens window is still shown after the lens was switched off");
    }

    /// <summary>
    /// As the app runs for a user: the lens in the topmost band, where it also looks after its place above
    /// menus and tooltips that open later.
    /// </summary>
    [WindowsFact]
    public void Outside_development_mode_the_lens_is_on_top_and_still_lets_the_pointer_through()
    {
        using var runtime = new LensRuntime(Options(FrameSourceKind.Pattern) with { DevMode = false });

        runtime.ToggleLens();
        WaitFor(() => runtime.Controller.State == LensState.Live, StartUp).ShouldBeTrue(Describe(runtime, "did not go live"));
        nint lens = FindWindow("ThemeLens.Lens", null);
        lens.ShouldNotBe(0, "there is no lens window");
        WaitFor(() => IsWindowVisible(lens), StartUp).ShouldBeTrue("the lens window is not shown");

        // Long enough for the lens to look after its place several times.
        Thread.Sleep(TimeSpan.FromSeconds(1.5));
        runtime.Controller.State.ShouldBe(LensState.Live, Describe(runtime, "did not stay live"));
        const int extendedStyle = -20;
        const long topmost = 0x0000_0008;
        (GetWindowLongPtr(lens, extendedStyle) & topmost).ShouldBe(topmost, "the lens is not in the topmost band");
        PixelRect bounds = runtime.LensBounds;
        var middle = new Point { X = bounds.X + (bounds.Width / 2), Y = bounds.Y + (bounds.Height / 2) };
        WindowFromPoint(middle).ShouldNotBe(lens, "the pointer must pass through the lens to whatever is underneath");

        runtime.EverythingOff();
        WaitFor(() => runtime.Controller.State == LensState.Off, StartUp).ShouldBeTrue(Describe(runtime, "did not switch off"));
        WaitFor(() => !IsWindowVisible(lens), StartUp).ShouldBeTrue("the lens window is still shown after everything was switched off");
    }

    /// <summary>FR-05: the lens fills the screen it is on, and goes back to its rectangle.</summary>
    [WindowsFact]
    public void Fill_this_screen_covers_the_whole_screen_and_goes_back()
    {
        using var runtime = new LensRuntime(Options(FrameSourceKind.Pattern) with { DevMode = false });
        runtime.ToggleLens();
        WaitFor(() => runtime.Controller.State == LensState.Live, StartUp).ShouldBeTrue(Describe(runtime, "did not go live"));
        PixelRect before = runtime.LensBounds;
        PixelRect screen = runtime.Screens[0].Bounds;

        runtime.ToggleFillScreen();
        WaitFor(() => runtime.LensBounds == screen, StartUp).ShouldBeTrue(
            $"the lens should cover the screen {screen}, but is at {runtime.LensBounds}");
        nint lens = FindWindow("ThemeLens.Lens", null);
        WaitFor(() => WindowRect(lens) == screen, StartUp).ShouldBeTrue(
            $"the lens window should cover the screen {screen}, but is at {WindowRect(lens)}");
        runtime.Controller.State.ShouldBe(LensState.Live, Describe(runtime, "did not survive filling the screen"));

        runtime.ToggleFillScreen();
        WaitFor(() => runtime.LensBounds == before, StartUp).ShouldBeTrue(
            $"the lens should be back at {before}, but is at {runtime.LensBounds}");
        runtime.EverythingOff();
    }

    /// <summary>FR-04, with the real pointer: in edit mode the lens is dragged to move it, and by an edge to resize it.</summary>
    [WindowsFact]
    public void In_edit_mode_the_pointer_moves_and_resizes_the_lens()
    {
        using var runtime = new LensRuntime(Options(FrameSourceKind.Pattern) with { DevMode = false });
        runtime.ToggleLens();
        WaitFor(() => runtime.Controller.State == LensState.Live, StartUp).ShouldBeTrue(Describe(runtime, "did not go live"));
        runtime.SetEditMode(true);
        WaitFor(() => runtime.Controller.State == LensState.Edit, StartUp).ShouldBeTrue(Describe(runtime, "did not enter edit mode"));
        Thread.Sleep(TimeSpan.FromSeconds(0.3));

        // Drag the middle of the lens: it moves.
        PixelRect start = runtime.LensBounds;
        DragPointer(start.X + (start.Width / 2), start.Y + (start.Height / 2), 60, 40);
        WaitFor(() => Near(runtime.LensBounds.X, start.X + 60) && Near(runtime.LensBounds.Y, start.Y + 40), StartUp).ShouldBeTrue(
            $"dragging the lens by 60 and 40 pixels should move it from {start} by that much, but it is at {runtime.LensBounds}");
        Near(runtime.LensBounds.Width, start.Width).ShouldBeTrue("moving the lens must not change its width");

        // Drag its right edge: it gets wider.
        PixelRect moved = runtime.LensBounds;
        DragPointer(moved.Right - 3, moved.Y + (moved.Height / 2), 80, 0);
        WaitFor(() => Near(runtime.LensBounds.Width, moved.Width + 80), StartUp).ShouldBeTrue(
            $"dragging the right edge by 80 pixels should widen the lens from {moved}, but it is at {runtime.LensBounds}");
        Near(runtime.LensBounds.X, moved.X).ShouldBeTrue("resizing by the right edge must not move the left edge");

        // Drag its bottom edge up: it gets shorter.
        PixelRect widened = runtime.LensBounds;
        DragPointer(widened.X + (widened.Width / 2), widened.Bottom - 3, 0, -50);
        WaitFor(() => Near(runtime.LensBounds.Height, widened.Height - 50), StartUp).ShouldBeTrue(
            $"dragging the bottom edge up by 50 pixels should shorten the lens from {widened}, but it is at {runtime.LensBounds}");

        runtime.SetEditMode(false);
        WaitFor(() => runtime.Controller.State == LensState.Live, StartUp).ShouldBeTrue(Describe(runtime, "did not leave edit mode"));
        runtime.EverythingOff();
    }

    /// <summary>With real screen capture, whichever way this machine offers.</summary>
    [WindowsFact]
    public void The_lens_goes_live_over_the_real_screen()
    {
        using var runtime = new LensRuntime(Options(FrameSourceKind.Auto));

        runtime.ToggleLens();
        WaitFor(() => runtime.Controller.State == LensState.Live, StartUp).ShouldBeTrue(Describe(runtime, "did not go live"));
        Thread.Sleep(TimeSpan.FromSeconds(1.5));
        runtime.Controller.State.ShouldBe(LensState.Live, Describe(runtime, "did not stay live"));

        runtime.EverythingOff();
        WaitFor(() => runtime.Controller.State == LensState.Off, StartUp).ShouldBeTrue(Describe(runtime, "did not switch off"));
    }

    private static LensRuntimeOptions Options(FrameSourceKind source) => new()
    {
        // Not topmost and at most a quarter of the screen, on the software rasteriser, with no global shortcuts taken.
        DevMode = true,
        Source = source,
        ForceSoftwareRendering = true,
        Hotkeys = [],
    };

    private static bool WaitFor(Func<bool> condition, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < limit)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }

    private static bool Near(int actual, int expected) => Math.Abs(actual - expected) <= 2;

    private static PixelRect WindowRect(nint window) =>
        GetWindowRect(window, out Rect r) ? new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top) : default;

    /// <summary>Presses the left button at a point of the screen, moves the pointer in steps, and lets go.</summary>
    private static void DragPointer(int x, int y, int byX, int byY)
    {
        const uint leftDown = 0x0002;
        const uint leftUp = 0x0004;
        SetCursorPos(x, y).ShouldBeTrue("the pointer could not be placed");
        Thread.Sleep(80);
        mouse_event(leftDown, 0, 0, 0, 0);
        Thread.Sleep(80);
        const int steps = 8;
        for (int i = 1; i <= steps; i++)
        {
            SetCursorPos(x + (byX * i / steps), y + (byY * i / steps));
            Thread.Sleep(30);
        }

        Thread.Sleep(80);
        mouse_event(leftUp, 0, 0, 0, 0);
        Thread.Sleep(80);
    }

    private static string Describe(LensRuntime runtime, string what)
    {
        LensStatus status = runtime.Controller.Status;
        return $"The lens {what}. State {status.State}, hold reason {status.HoldReason}, capture problem "
            + $"'{status.CaptureProblem ?? "none"}', reduced speed {status.ReducedSpeed}, {runtime.FramesPerSecond:0.0} frames per second, "
            + $"lens at {runtime.LensBounds}, {runtime.Screens.Count} screen(s).";
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    private static partial nint WindowFromPoint(Point point);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint window, out Rect rect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("user32.dll")]
    private static partial void mouse_event(uint flags, int dx, int dy, uint data, nuint extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
