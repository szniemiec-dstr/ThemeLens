// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Engine;
using ThemeLens.Platform.Windows;

namespace ThemeLens.Platform.Tests;

/// <summary>
/// The lens window has the styles section 7.3 lists, and edit mode changes exactly one of them. These tests
/// make real windows (they are never shown), so they need a desktop session.
/// </summary>
[Trait("Category", "Ui")]
public sealed class LensWindowTests
{
    // The values of winuser.h, written out so the test does not take the product's word for them.
    private const uint Topmost = 0x0000_0008;
    private const uint Transparent = 0x0000_0020;
    private const uint ToolWindow = 0x0000_0080;
    private const uint Layered = 0x0008_0000;
    private const uint NoRedirectionBitmap = 0x0020_0000;
    private const uint NoActivate = 0x0800_0000;
    private const uint Locked = Layered | Transparent | Topmost | NoActivate | ToolWindow | NoRedirectionBitmap;

    private static readonly PixelRect Somewhere = new(40, 40, 320, 200);

    [WindowsFact]
    public void The_locked_lens_has_exactly_the_styles_of_the_specification()
    {
        var window = new LensWindow(devMode: false, Somewhere);
        try
        {
            StylesOf(window).ShouldBe(Locked, Explain(StylesOf(window), Locked));
            window.IsEditMode.ShouldBeFalse();
        }
        finally
        {
            window.Destroy();
        }
    }

    [WindowsFact]
    public void Edit_mode_takes_away_the_transparent_style_and_nothing_else()
    {
        var window = new LensWindow(devMode: false, Somewhere);
        try
        {
            window.SetEditMode(true);
            StylesOf(window).ShouldBe(Locked & ~Transparent, Explain(StylesOf(window), Locked & ~Transparent));
            window.IsEditMode.ShouldBeTrue();

            window.SetEditMode(false);
            StylesOf(window).ShouldBe(Locked, Explain(StylesOf(window), Locked));
        }
        finally
        {
            window.Destroy();
        }
    }

    [WindowsFact]
    public void In_development_mode_the_lens_is_not_topmost()
    {
        var window = new LensWindow(devMode: true, Somewhere);
        try
        {
            StylesOf(window).ShouldBe(Locked & ~Topmost, Explain(StylesOf(window), Locked & ~Topmost));
        }
        finally
        {
            window.Destroy();
        }
    }

    [WindowsFact]
    public void The_lens_is_kept_out_of_screen_capture()
    {
        var window = new LensWindow(devMode: false, Somewhere);
        try
        {
            CaptureExclusion.IsApplied(window.Handle).ShouldBeTrue(
                "The lens window is not excluded from capture, so the lens would capture its own picture.");
        }
        finally
        {
            window.Destroy();
        }
    }

    [WindowsFact]
    public void A_destroyed_lens_window_has_no_handle()
    {
        var window = new LensWindow(devMode: false, Somewhere);
        window.Handle.ShouldNotBe(0);

        window.Destroy();

        window.Handle.ShouldBe(0);
    }

    private static uint StylesOf(LensWindow window) => (uint)window.ExtendedStyle;

    private static string Explain(uint actual, uint expected) =>
        $"Extended styles are 0x{actual:X8}, expected 0x{expected:X8}; the bits that differ are 0x{actual ^ expected:X8}.";
}
