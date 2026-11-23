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

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }
}
