// SPDX-License-Identifier: GPL-3.0-or-later
using System.Diagnostics;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace ThemeLens.UiTests;

/// <summary>
/// The app itself, started as a user starts it and driven the way a screen reader drives it: through UI
/// Automation (section 9.1, UiTests). The app runs in development mode over the built-in test pattern, so the
/// lens is small, not on top, and no screen capture is involved.
/// </summary>
[Trait("Category", "Ui")]
public sealed partial class ControlWindowTests
{
    private const string LensWindowClass = "ThemeLens.Lens";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(20);

    [WindowsFact]
    public void The_app_starts_and_shows_its_control_window()
    {
        using var session = AppSession.Start();

        session.Window.Title.ShouldBe("ThemeLens");
        session.Status().ShouldBe("Off");
        session.Process.HasExited.ShouldBeFalse();

        // These tests start the app in development mode, and the window has to say so where nobody misses it:
        // in that mode the lens is not kept on top and stays small.
        session.Window.FindFirstDescendant(c => c.ByName("Development mode")).ShouldNotBeNull(
            "the control window should announce development mode");
    }

    /// <summary>Section 4.8: every control has a UI Automation name, so Narrator and NVDA can drive the whole app.</summary>
    [WindowsFact]
    public void Every_control_that_takes_the_keyboard_has_a_name()
    {
        using var session = AppSession.Start();

        var nameless = new List<string>();
        int controls = 0;
        foreach (AutomationElement element in session.Window.FindAllDescendants())
        {
            if (!element.Properties.IsKeyboardFocusable.ValueOrDefault || !element.Properties.IsControlElement.ValueOrDefault)
            {
                continue;
            }

            controls++;
            if (string.IsNullOrWhiteSpace(element.Properties.Name.ValueOrDefault))
            {
                nameless.Add($"{element.Properties.ControlType.ValueOrDefault} '{element.Properties.AutomationId.ValueOrDefault}'");
            }
        }

        controls.ShouldBeGreaterThanOrEqualTo(4, "the lens switch, the theme list, fill screen and edit mode take the keyboard");
        nameless.ShouldBeEmpty("these controls have no name for a screen reader: " + string.Join(", ", nameless));
    }

    [WindowsFact]
    public void The_lens_switch_turns_the_lens_on_and_off()
    {
        using var session = AppSession.Start();
        AutomationElement lensSwitch = session.Find("LensSwitch");
        lensSwitch.Properties.Name.ValueOrDefault.ShouldBe("Lens on or off");

        lensSwitch.Patterns.Toggle.Pattern.Toggle();

        WaitFor(() => session.Status().StartsWith("Live", StringComparison.Ordinal)).ShouldBeTrue(
            $"the status should read Live once the lens is on, but reads '{session.Status()}'");
        WaitFor(() => IsShown(LensWindowClass)).ShouldBeTrue("the lens window should be on screen");
        lensSwitch.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.On);

        lensSwitch.Patterns.Toggle.Pattern.Toggle();

        WaitFor(() => session.Status() == "Off").ShouldBeTrue($"the status should read Off again, but reads '{session.Status()}'");
        WaitFor(() => !IsShown(LensWindowClass)).ShouldBeTrue("the lens window should be gone");
    }

    [WindowsFact]
    public void Choosing_another_theme_keeps_the_lens_live()
    {
        using var session = AppSession.Start();
        session.Find("LensSwitch").Patterns.Toggle.Pattern.Toggle();
        WaitFor(() => session.Status().StartsWith("Live", StringComparison.Ordinal)).ShouldBeTrue(
            $"the status should read Live, but reads '{session.Status()}'");

        // Open the list, as a screen reader would, and choose the last theme in it.
        AutomationElement themes = session.Find("ThemeList");
        themes.Patterns.ExpandCollapse.Pattern.Expand();
        AutomationElement[] items = [];
        WaitFor(() => (items = themes.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))).Length >= 13).ShouldBeTrue(
            $"the list should offer the thirteen built-in themes, but offers {items.Length}");
        AutomationElement last = items[^1];
        last.Patterns.SelectionItem.Pattern.IsSelected.Value.ShouldBeFalse("the last theme should not be the one the app starts with");
        last.Patterns.SelectionItem.Pattern.Select();
        WaitFor(() => last.Patterns.SelectionItem.Pattern.IsSelected.Value).ShouldBeTrue("the theme list should show the new choice");
        themes.Patterns.ExpandCollapse.Pattern.Collapse();

        Thread.Sleep(TimeSpan.FromSeconds(1));
        session.Status().ShouldStartWith("Live");
        session.Process.HasExited.ShouldBeFalse();
    }

    [WindowsFact]
    public void Closing_the_control_window_ends_the_app_and_removes_the_lens()
    {
        using var session = AppSession.Start();
        session.Find("LensSwitch").Patterns.Toggle.Pattern.Toggle();
        WaitFor(() => IsShown(LensWindowClass)).ShouldBeTrue("the lens window should be on screen");

        session.Window.Close();

        WaitFor(() => session.Process.HasExited).ShouldBeTrue("the app should end when its control window is closed");
        session.Process.ExitCode.ShouldBe(0);
        WaitFor(() => !IsShown(LensWindowClass)).ShouldBeTrue("the lens must not outlive the app");
    }

    private static bool WaitFor(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < Patience)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(50);
        }

        return condition();
    }

    private static bool IsShown(string windowClass)
    {
        nint window = FindWindow(windowClass, null);
        return window != 0 && IsWindowVisible(window);
    }

    [LibraryImport("user32.dll", EntryPoint = "FindWindowW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint FindWindow(string className, string? windowName);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint window);

    /// <summary>One run of the app, from start to the end of a test.</summary>
    private sealed class AppSession : IDisposable
    {
        private readonly Application _app;
        private readonly UIA3Automation _automation;

        private AppSession(Process process, Application app, UIA3Automation automation, Window window)
        {
            Process = process;
            _app = app;
            _automation = automation;
            Window = window;
        }

        /// <summary>The app's process, started here, so that its end and its exit code can be asked for.</summary>
        public Process Process { get; }

        public Window Window { get; }

        public static AppSession Start()
        {
            string executable = AppPath();
            File.Exists(executable).ShouldBeTrue($"the app has not been built: {executable}");

            Process process = Process.Start(new ProcessStartInfo(executable, "--dev --source pattern") { UseShellExecute = false })
                ?? throw new InvalidOperationException("The app could not be started.");
            var automation = new UIA3Automation();
            Application? app = null;
            try
            {
                app = Application.Attach(process);
                Window? window = app.GetMainWindow(automation, Patience);
                if (window is null)
                {
                    string ended = process.HasExited ? $"the app ended with exit code {process.ExitCode}" : "the app is still running";
                    throw new InvalidOperationException($"The control window did not appear within {Patience.TotalSeconds:0} seconds; {ended}.");
                }

                return new AppSession(process, app, automation, window);
            }
            catch
            {
                Stop(process);
                app?.Dispose();
                automation.Dispose();
                throw;
            }
        }

        /// <summary>An element of the control window by the name it has in the XAML.</summary>
        public AutomationElement Find(string automationId) =>
            Window.FindFirstDescendant(c => c.ByAutomationId(automationId))
            ?? throw new InvalidOperationException($"The control window has no element called '{automationId}'.");

        /// <summary>What the status line reads.</summary>
        public string Status() => Find("StatusLine").Properties.Name.ValueOrDefault ?? string.Empty;

        public void Dispose()
        {
            Stop(Process);
            _app.Dispose();
            _automation.Dispose();
            Process.Dispose();
        }

        private static void Stop(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
            }
            catch (InvalidOperationException)
            {
                // It ended between the question and the kill.
            }
        }

        /// <summary>The app's executable, built in the same configuration and for the same framework as these tests.</summary>
        private static string AppPath()
        {
            // .../tests/ThemeLens.UiTests/bin/<configuration>/<framework>/
            var here = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string framework = here.Name;
            string configuration = here.Parent!.Name;
            string root = here.Parent!.Parent!.Parent!.Parent!.Parent!.FullName;
            return Path.Combine(root, "src", "ThemeLens.App", "bin", configuration, framework, "ThemeLens.App.exe");
        }
    }
}
