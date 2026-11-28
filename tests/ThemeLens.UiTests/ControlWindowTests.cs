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
        WaitFor(() => (items = themes.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))).Length >= 16).ShouldBeTrue(
            $"the list should offer the sixteen built-in themes, but offers {items.Length}");
        AutomationElement last = items[^1];
        last.Patterns.SelectionItem.Pattern.IsSelected.Value.ShouldBeFalse("the last theme should not be the one the app starts with");
        last.Patterns.SelectionItem.Pattern.Select();
        WaitFor(() => last.Patterns.SelectionItem.Pattern.IsSelected.Value).ShouldBeTrue("the theme list should show the new choice");
        themes.Patterns.ExpandCollapse.Pattern.Collapse();

        Thread.Sleep(TimeSpan.FromSeconds(1));
        session.Status().ShouldStartWith("Live");
        session.Process.HasExited.ShouldBeFalse();
    }

    /// <summary>FR-14: the sliders change the lens while it is live, and one button goes back to the theme's own values.</summary>
    [WindowsFact]
    public void A_slider_changes_the_theme_and_the_reset_button_takes_it_back()
    {
        using var session = AppSession.Start();
        session.Find("LensSwitch").Patterns.Toggle.Pattern.Toggle();
        WaitFor(() => session.Status().StartsWith("Live", StringComparison.Ordinal)).ShouldBeTrue(
            $"the status should read Live, but reads '{session.Status()}'");

        AutomationElement brightness = session.Find("BrightnessSlider");
        brightness.Properties.Name.ValueOrDefault.ShouldBe("Brightness limit");
        AutomationElement reset = session.Find("ResetButton");
        reset.Properties.IsEnabled.ValueOrDefault.ShouldBeFalse("nothing has been changed yet, so there is nothing to take back");
        double original = brightness.Patterns.RangeValue.Pattern.Value.Value;

        brightness.Patterns.RangeValue.Pattern.SetValue(0.3);

        WaitFor(() => reset.Properties.IsEnabled.ValueOrDefault).ShouldBeTrue("after a change the reset button should be available");
        Thread.Sleep(TimeSpan.FromSeconds(1));
        session.Status().ShouldStartWith("Live");

        reset.Patterns.Invoke.Pattern.Invoke();

        WaitFor(() => Math.Abs(brightness.Patterns.RangeValue.Pattern.Value.Value - original) < 0.005).ShouldBeTrue(
            $"the slider should be back at {original:0.00}, but is at {brightness.Patterns.RangeValue.Pattern.Value.Value:0.00}");
        WaitFor(() => !reset.Properties.IsEnabled.ValueOrDefault).ShouldBeTrue("after going back there is nothing to take back");
    }

    /// <summary>FR-08, FR-09: the table lists the colours under the lens; choosing a role pins it until the mapping is reset.</summary>
    [WindowsFact]
    public void The_mapping_table_lists_the_colours_and_a_chosen_role_is_pinned_until_reset()
    {
        using var session = AppSession.Start();
        session.Find("MappingEmpty").Properties.Name.ValueOrDefault.ShouldBe("Switch the lens on to see the colours under it.");
        session.Find("LensSwitch").Patterns.Toggle.Pattern.Toggle();
        WaitFor(() => session.Status().StartsWith("Live", StringComparison.Ordinal)).ShouldBeTrue(
            $"the status should read Live, but reads '{session.Status()}'");

        // One role list per colour found under the lens.
        AutomationElement[] roleLists = [];
        WaitFor(() => (roleLists = RoleLists(session)).Length >= 2).ShouldBeTrue(
            $"the table should list the colours of the test pattern, but has {roleLists.Length} rows");
        AutomationElement reset = session.Find("ResetMappingButton");
        reset.Properties.IsEnabled.ValueOrDefault.ShouldBeFalse("no role has been chosen yet, so there is no mapping to reset");

        // Give one colour another role than it has. The table may put its rows in a new order between looking
        // and opening (two colours of the same share swap places), so a list that came to nothing is looked up again.
        AutomationElement[] roles = [];
        AutomationElement? list = null;
        for (int attempt = 0; attempt < 5 && roles.Length != 9; attempt++)
        {
            roleLists = RoleLists(session);
            if (roleLists.Length < 2)
            {
                Thread.Sleep(300);
                continue;
            }

            list = roleLists[1];
            try
            {
                list.Patterns.ExpandCollapse.Pattern.Expand();
                AutomationElement open = list;
                WaitFor(() => (roles = open.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))).Length == 9, TimeSpan.FromSeconds(3));
            }
            catch (Exception e) when (e is System.Runtime.InteropServices.COMException or InvalidOperationException or FlaUI.Core.Exceptions.FlaUIException)
            {
                // The row was replaced while it was being opened.
                roles = [];
            }
        }

        roles.Length.ShouldBe(9, "an open role list should offer the nine roles");
        AutomationElement other = roles.First(role => !role.Patterns.SelectionItem.Pattern.IsSelected.Value);
        other.Patterns.SelectionItem.Pattern.Select();
        list!.Patterns.ExpandCollapse.Pattern.Collapse();

        WaitFor(() => reset.Properties.IsEnabled.ValueOrDefault).ShouldBeTrue("a chosen role should make \"Reset mapping\" available");
        Thread.Sleep(TimeSpan.FromSeconds(1));
        session.Status().ShouldStartWith("Live");

        reset.Patterns.Invoke.Pattern.Invoke();
        WaitFor(() => !reset.Properties.IsEnabled.ValueOrDefault).ShouldBeTrue("after the reset there is nothing left to reset");
        session.Status().ShouldStartWith("Live");
    }

    /// <summary>FR-15: what the sliders show can be saved as a theme of one's own, which is still there at the next start.</summary>
    [WindowsFact]
    public void The_users_values_can_be_saved_as_a_theme_of_their_own()
    {
        const string name = "Evening reading";
        string folder = Path.Combine(Path.GetTempPath(), "ThemeLens.UiTests." + Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = AppSession.Start(folder))
            {
                AutomationElement save = first.Find("SaveThemeButton");
                save.Properties.IsEnabled.ValueOrDefault.ShouldBeFalse("a theme needs a name before it can be saved");
                first.Find("BrightnessSlider").Patterns.RangeValue.Pattern.SetValue(0.4);
                AutomationElement box = first.Find("NewThemeName");
                box.Properties.Name.ValueOrDefault.ShouldBe("Name for your theme");
                box.Patterns.Value.Pattern.SetValue(name);
                WaitFor(() => save.Properties.IsEnabled.ValueOrDefault).ShouldBeTrue("with a name typed, the theme can be saved");

                save.Patterns.Invoke.Pattern.Invoke();

                WaitFor(() => SelectedTheme(first) == name).ShouldBeTrue(
                    $"the saved theme should be the chosen one, but the list shows '{SelectedTheme(first)}'");
                first.Find("BrightnessSlider").Patterns.RangeValue.Pattern.Value.Value.ShouldBe(0.4, 0.01);
                first.Find("ResetButton").Properties.IsEnabled.ValueOrDefault.ShouldBeFalse(
                    "the values are the new theme's own now, so there is nothing to go back from");
                first.Window.Close();
                WaitFor(() => first.Process.HasExited).ShouldBeTrue("the app should end when its control window is closed");
            }

            using var second = AppSession.Start(folder);
            SelectedTheme(second).ShouldBe(name);
            second.Find("BrightnessSlider").Patterns.RangeValue.Pattern.Value.Value.ShouldBe(0.4, 0.01);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// FR-26 to FR-29: "rectangles only" has a switch, a choice between bright and dark rectangles, one bar with
    /// two handles for the sizes in pixels, a slider for the brightness, and three switches kept for each theme.
    /// The handles cannot pass each other, the lens stays live, and the values come back at the next start.
    /// </summary>
    [WindowsFact]
    public void Rectangles_only_has_a_switch_a_target_two_size_handles_and_a_brightness_slider()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ThemeLens.UiTests." + Guid.NewGuid().ToString("N"));
        try
        {
            using (var first = AppSession.Start(folder))
            {
                first.Find("LensSwitch").Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => first.Status().StartsWith("Live", StringComparison.Ordinal)).ShouldBeTrue(
                    $"the status should read Live, but reads '{first.Status()}'");

                AutomationElement only = first.Find("RectanglesSwitch");
                only.Properties.Name.ValueOrDefault.ShouldBe("Only change bright rectangles");
                only.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.Off, "the option starts switched off");
                AutomationElement smallest = first.Find("RectangleSmallestSlider");
                AutomationElement full = first.Find("RectangleFullSlider");
                AutomationElement brightness = first.Find("RectangleBrightnessSlider");
                smallest.Properties.Name.ValueOrDefault.ShouldBe("Smallest size");
                full.Properties.Name.ValueOrDefault.ShouldBe("Size for the brightness below");
                brightness.Properties.Name.ValueOrDefault.ShouldBe("At least this bright");
                first.Find("RectangleTargetList").Properties.Name.ValueOrDefault.ShouldBe("Look for");

                // The bar runs from a square of 10 pixels to the square that covers half the screen.
                smallest.Patterns.RangeValue.Pattern.Minimum.Value.ShouldBe(10.0, 0.001);
                double largest = full.Patterns.RangeValue.Pattern.Maximum.Value;
                largest.ShouldBeGreaterThan(300.0, "half of any screen is larger than a square of 300 pixels");
                smallest.Patterns.RangeValue.Pattern.Value.Value.ShouldBe(200.0, 0.5);
                brightness.Patterns.RangeValue.Pattern.Minimum.Value.ShouldBe(0.50, 0.001);
                brightness.Patterns.RangeValue.Pattern.Maximum.Value.ShouldBe(1.0, 0.001);

                // The two preview settings are on, and can be switched off.
                AutomationElement ownTarget = first.Find("RectangleOwnTargetSwitch");
                AutomationElement plain = first.Find("RectanglePlainSwitch");
                AutomationElement keepHues = first.Find("RectangleKeepHuesSwitch");
                ownTarget.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.On);
                plain.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.On);
                keepHues.Properties.Name.ValueOrDefault.ShouldBe("Keep hues");
                keepHues.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.Off, "the plain negative is the default");

                only.Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => only.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On).ShouldBeTrue("the switch should be on");
                smallest.Patterns.RangeValue.Pattern.SetValue(40);
                full.Patterns.RangeValue.Pattern.SetValue(240);
                brightness.Patterns.RangeValue.Pattern.SetValue(0.85);

                string help = string.Empty;
                WaitFor(() => (help = first.Find("RectanglesHelp").Properties.Name.ValueOrDefault ?? string.Empty).Contains("85%", StringComparison.Ordinal))
                    .ShouldBeTrue($"the explanation should name the brightness, but reads '{help}'");
                help.ShouldContain("240 pixels");
                help.ShouldContain("40 pixels");

                // The left handle pushes the right one along.
                smallest.Patterns.RangeValue.Pattern.SetValue(280);
                WaitFor(() => Math.Abs(full.Patterns.RangeValue.Pattern.Value.Value - 280) < 1).ShouldBeTrue(
                    $"the right handle should have been pushed to 280, but is at {full.Patterns.RangeValue.Pattern.Value.Value:0}");

                // Without "choose myself" the theme decides: the app starts with a dark theme, so bright it is, and the list is greyed out.
                ownTarget.Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => !first.Find("RectangleTargetList").Properties.IsEnabled.ValueOrDefault).ShouldBeTrue(
                    "while the theme decides, the list cannot be used");
                plain.Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => plain.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off).ShouldBeTrue();
                keepHues.Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => keepHues.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On).ShouldBeTrue();

                // These three are kept theme by theme: another theme still has the defaults, and coming back
                // brings the choices back.
                string original = SelectedTheme(first);
                AutomationElement themes = first.Find("ThemeList");
                void Choose(Func<AutomationElement, bool> which)
                {
                    themes.Patterns.ExpandCollapse.Pattern.Expand();
                    AutomationElement[] items = [];
                    WaitFor(() => (items = themes.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))).Length >= 16).ShouldBeTrue(
                        $"the list should offer the sixteen built-in themes, but offers {items.Length}");
                    items.First(which).Patterns.SelectionItem.Pattern.Select();
                    themes.Patterns.ExpandCollapse.Pattern.Collapse();
                }

                Choose(item => !item.Patterns.SelectionItem.Pattern.IsSelected.Value);
                WaitFor(() => SelectedTheme(first) != original).ShouldBeTrue("another theme should be chosen now");
                WaitFor(() => keepHues.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.Off).ShouldBeTrue(
                    "the other theme has its own choices: hues not kept");
                ownTarget.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.On);
                plain.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.On);
                only.Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.On, "the option itself is the same under every theme");
                Choose(item => item.Properties.Name.ValueOrDefault == original);
                WaitFor(() => SelectedTheme(first) == original).ShouldBeTrue(
                    $"the first theme should be chosen again, but the list shows '{SelectedTheme(first)}'");
                WaitFor(() => keepHues.Patterns.Toggle.Pattern.ToggleState.Value == ToggleState.On).ShouldBeTrue(
                    "coming back to the theme brings its choices back");

                Thread.Sleep(TimeSpan.FromSeconds(1));
                first.Status().ShouldStartWith("Live");
                first.Window.Close();
                WaitFor(() => first.Process.HasExited).ShouldBeTrue("the app should end when its control window is closed");
            }

            using var second = AppSession.Start(folder);
            second.Find("RectanglesSwitch").Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.On);
            second.Find("RectangleSmallestSlider").Patterns.RangeValue.Pattern.Value.Value.ShouldBe(280, 1);
            second.Find("RectangleFullSlider").Patterns.RangeValue.Pattern.Value.Value.ShouldBe(280, 1);
            second.Find("RectangleBrightnessSlider").Patterns.RangeValue.Pattern.Value.Value.ShouldBe(0.85, 0.01);
            second.Find("RectangleOwnTargetSwitch").Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.Off);
            second.Find("RectanglePlainSwitch").Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.Off);
            second.Find("RectangleKeepHuesSwitch").Patterns.Toggle.Pattern.ToggleState.Value.ShouldBe(ToggleState.On);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>FR-18: the theme, and that the lens was on, are remembered from one run to the next.</summary>
    [WindowsFact]
    public void The_next_start_brings_back_the_theme_and_the_lens()
    {
        string folder = Path.Combine(Path.GetTempPath(), "ThemeLens.UiTests." + Guid.NewGuid().ToString("N"));
        try
        {
            string chosen;
            using (var first = AppSession.Start(folder))
            {
                AutomationElement themes = first.Find("ThemeList");
                themes.Patterns.ExpandCollapse.Pattern.Expand();
                AutomationElement[] items = [];
                WaitFor(() => (items = themes.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))).Length >= 16).ShouldBeTrue(
                    $"the list should offer the sixteen built-in themes, but offers {items.Length}");
                AutomationElement last = items[^1];
                chosen = last.Properties.Name.ValueOrDefault ?? string.Empty;
                last.Patterns.SelectionItem.Pattern.Select();
                themes.Patterns.ExpandCollapse.Pattern.Collapse();

                first.Find("LensSwitch").Patterns.Toggle.Pattern.Toggle();
                WaitFor(() => first.Status().StartsWith("Live", StringComparison.Ordinal)).ShouldBeTrue(
                    $"the status should read Live, but reads '{first.Status()}'");

                // Closing the app is not switching the lens off.
                first.Window.Close();
                WaitFor(() => first.Process.HasExited).ShouldBeTrue("the app should end when its control window is closed");
            }

            using var second = AppSession.Start(folder);
            WaitFor(() => second.Status().StartsWith("Live", StringComparison.Ordinal)).ShouldBeTrue(
                $"the lens was on when the app was closed, so it should come back by itself; the status reads '{second.Status()}'");
            AutomationElement list = second.Find("ThemeList");
            list.Patterns.ExpandCollapse.Pattern.Expand();
            AutomationElement[] again = [];
            WaitFor(() => (again = list.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))).Length >= 16).ShouldBeTrue(
                "the list should offer the themes again");
            again[^1].Properties.Name.ValueOrDefault.ShouldBe(chosen);
            again[^1].Patterns.SelectionItem.Pattern.IsSelected.Value.ShouldBeTrue($"the theme '{chosen}' should still be the chosen one");
            list.Patterns.ExpandCollapse.Pattern.Collapse();
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
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

    /// <summary>The name of the theme the theme list shows as chosen, read by opening the list as a screen reader would.</summary>
    private static string SelectedTheme(AppSession session)
    {
        AutomationElement list = session.Find("ThemeList");
        list.Patterns.ExpandCollapse.Pattern.Expand();
        AutomationElement[] items = [];
        WaitFor(() => (items = list.FindAllDescendants(c => c.ByControlType(ControlType.ListItem))).Length >= 13, TimeSpan.FromSeconds(5));
        string chosen = items.FirstOrDefault(item => item.Patterns.SelectionItem.Pattern.IsSelected.Value)?.Properties.Name.ValueOrDefault ?? string.Empty;
        list.Patterns.ExpandCollapse.Pattern.Collapse();
        return chosen;
    }

    /// <summary>The "Used as" lists of the mapping table, one per row.</summary>
    private static AutomationElement[] RoleLists(AppSession session) =>
        [.. session.Window.FindAllDescendants(c => c.ByControlType(ControlType.ComboBox))
            .Where(list => (list.Properties.Name.ValueOrDefault ?? string.Empty).StartsWith("Used as, for ", StringComparison.Ordinal))];

    private static bool WaitFor(Func<bool> condition) => WaitFor(condition, Patience);

    private static bool WaitFor(Func<bool> condition, TimeSpan limit)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < limit)
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
        private readonly string? _ownFolder;

        private AppSession(Process process, Application app, UIA3Automation automation, Window window, string? ownFolder)
        {
            Process = process;
            _app = app;
            _automation = automation;
            Window = window;
            _ownFolder = ownFolder;
        }

        /// <summary>The app's process, started here, so that its end and its exit code can be asked for.</summary>
        public Process Process { get; }

        public Window Window { get; }

        /// <param name="dataFolder">
        /// Where this run of the app keeps its settings. Left out, the run gets a folder of its own that is
        /// removed afterwards, so a test never sees, or changes, what the person at this machine has set.
        /// </param>
        public static AppSession Start(string? dataFolder = null)
        {
            string executable = AppPath();
            File.Exists(executable).ShouldBeTrue($"the app has not been built: {executable}");

            string? ownFolder = dataFolder is null ? Path.Combine(Path.GetTempPath(), "ThemeLens.UiTests." + Guid.NewGuid().ToString("N")) : null;
            var start = new ProcessStartInfo(executable, "--dev --source pattern") { UseShellExecute = false };
            start.Environment["THEMELENS_DATA"] = dataFolder ?? ownFolder;
            Process process = Process.Start(start) ?? throw new InvalidOperationException("The app could not be started.");
            var automation = new UIA3Automation();
            Application? app = null;
            try
            {
                // By its id: the automation library then keeps a process object of its own, and closes that one,
                // not ours, when it is done.
                app = Application.Attach(process.Id);
                Window? window = app.GetMainWindow(automation, Patience);
                if (window is null)
                {
                    string ended = process.HasExited ? $"the app ended with exit code {process.ExitCode}" : "the app is still running";
                    throw new InvalidOperationException($"The control window did not appear within {Patience.TotalSeconds:0} seconds; {ended}.");
                }

                return new AppSession(process, app, automation, window, ownFolder);
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
            if (_ownFolder is not null && Directory.Exists(_ownFolder))
            {
                try
                {
                    Directory.Delete(_ownFolder, recursive: true);
                }
                catch (IOException)
                {
                    // A file still held for a moment by the app that was just stopped. The folder is in the temporary directory.
                }
            }
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
