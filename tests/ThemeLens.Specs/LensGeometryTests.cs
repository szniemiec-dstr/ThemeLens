// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Engine;

namespace ThemeLens.Specs;

/// <summary>Moving, resizing and filling a screen (section 4.5), without any windows.</summary>
public class LensGeometryTests
{
    private static readonly ScreenInfo Primary = new(1, new PixelRect(0, 0, 1920, 1080), IsPrimary: true);
    private static readonly ScreenInfo Right = new(2, new PixelRect(1920, 0, 2560, 1440), IsPrimary: false);
    private static readonly ScreenInfo Above = new(3, new PixelRect(0, -1200, 1920, 1200), IsPrimary: false);
    private static readonly ScreenInfo[] Screens = [Primary, Right, Above];

    [Fact]
    public void The_lens_belongs_to_the_screen_it_is_mostly_on()
    {
        LensGeometry.ScreenFor(new PixelRect(100, 100, 400, 300), Screens).ShouldBe(Primary);
        LensGeometry.ScreenFor(new PixelRect(1800, 100, 400, 300), Screens).ShouldBe(Right);
        LensGeometry.ScreenFor(new PixelRect(1600, 100, 400, 300), Screens).ShouldBe(Primary);
        LensGeometry.ScreenFor(new PixelRect(100, -700, 400, 300), Screens).ShouldBe(Above);
    }

    [Fact]
    public void A_lens_left_on_a_screen_that_was_unplugged_goes_to_the_nearest_one()
    {
        LensGeometry.ScreenFor(new PixelRect(6000, 200, 400, 300), [Primary, Above]).ShouldBe(Primary);
    }

    [Fact]
    public void Fill_this_screen_covers_the_whole_monitor_and_the_same_command_restores_the_rectangle()
    {
        var window = new PixelRect(2000, 100, 800, 600);
        var placement = new LensPlacement(window, FillsScreen: false);
        placement.Current(Screens).ShouldBe(window);

        LensPlacement filled = placement.ToggleFill();
        filled.Current(Screens).ShouldBe(Right.Bounds);

        filled.ToggleFill().Current(Screens).ShouldBe(window);
    }

    [Fact]
    public void Moving_keeps_the_lens_on_one_screen()
    {
        var lens = new PixelRect(100, 100, 400, 300);
        LensGeometry.Move(lens, -500, -50, [Primary]).ShouldBe(new PixelRect(0, 50, 400, 300));
        LensGeometry.Move(lens, 5000, 5000, [Primary]).ShouldBe(new PixelRect(1520, 780, 400, 300));
    }

    [Fact]
    public void Moving_far_enough_takes_the_lens_to_the_next_screen()
    {
        var lens = new PixelRect(1500, 100, 400, 300);
        PixelRect moved = LensGeometry.Move(lens, 300, 0, Screens);
        LensGeometry.ScreenFor(moved, Screens).ShouldBe(Right);
        moved.X.ShouldBeGreaterThanOrEqualTo(Right.Bounds.X);
    }

    [Fact]
    public void Resizing_respects_the_minimum_size_and_the_screen_edge()
    {
        var lens = new PixelRect(100, 100, 400, 300);
        LensGeometry.Resize(lens, -1000, -1000, [Primary]).ShouldBe(new PixelRect(100, 100, LensGeometry.MinimumSize, LensGeometry.MinimumSize));
        LensGeometry.Resize(lens, 5000, 5000, [Primary]).ShouldBe(new PixelRect(100, 100, 1820, 980));
        LensGeometry.Resize(lens, 16, 0, [Primary]).ShouldBe(new PixelRect(100, 100, 416, 300));
    }

    [Fact]
    public void Dev_mode_limits_the_lens_to_a_quarter_of_the_screen()
    {
        PixelRect limited = LensGeometry.LimitForDevMode(Primary.Bounds, Primary.Bounds);
        ((long)limited.Width * limited.Height).ShouldBeLessThanOrEqualTo((long)Primary.Bounds.Width * Primary.Bounds.Height / 4);

        var small = new PixelRect(10, 10, 300, 200);
        LensGeometry.LimitForDevMode(small, Primary.Bounds).ShouldBe(small);
    }

    [Fact]
    public void Screens_are_described_by_number_and_position()
    {
        LensGeometry.PositionOf(Primary, Screens).ShouldBe(ScreenPosition.Primary);
        LensGeometry.PositionOf(Right, Screens).ShouldBe(ScreenPosition.Right);
        LensGeometry.PositionOf(Above, Screens).ShouldBe(ScreenPosition.Above);
        LensGeometry.PositionOf(new ScreenInfo(4, new PixelRect(-1280, 0, 1280, 1024), false), Screens).ShouldBe(ScreenPosition.Left);
        LensGeometry.PositionOf(new ScreenInfo(5, new PixelRect(0, 1080, 1920, 1080), false), Screens).ShouldBe(ScreenPosition.Below);
    }

    [Fact]
    public void A_monitor_layout_has_a_stable_key_whatever_order_the_screens_are_listed_in()
    {
        LensGeometry.LayoutKey([Primary, Right]).ShouldBe(LensGeometry.LayoutKey([Right, Primary]));
        LensGeometry.LayoutKey([Primary, Right]).ShouldBe("1920x1080@0,0|2560x1440@1920,0");
        LensGeometry.LayoutKey([Primary]).ShouldNotBe(LensGeometry.LayoutKey([Primary, Right]));
    }

    [Fact]
    public void The_first_rectangle_is_the_middle_of_the_screen()
    {
        LensGeometry.DefaultFor(Primary.Bounds).ShouldBe(new PixelRect(480, 270, 960, 540));
    }
}
