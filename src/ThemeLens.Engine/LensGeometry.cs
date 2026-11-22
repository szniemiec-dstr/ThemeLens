// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;

namespace ThemeLens.Engine;

/// <summary>One monitor: its number as Windows shows it, and its full bounds including the taskbar, in physical pixels.</summary>
public sealed record ScreenInfo(int Number, PixelRect Bounds, bool IsPrimary);

/// <summary>Where a screen sits relative to the primary one; the control window lists screens as "Screen 2, right".</summary>
public enum ScreenPosition
{
    Primary,
    Left,
    Right,
    Above,
    Below,
}

/// <summary>Where the lens is and how it got there (section 4.5). Pure arithmetic, no windows.</summary>
public static class LensGeometry
{
    /// <summary>The lens cannot be made smaller than this in either direction.</summary>
    public const int MinimumSize = 96;

    /// <summary>The screen the lens is mostly on. With no overlap at all, the nearest one.</summary>
    public static ScreenInfo ScreenFor(PixelRect lens, IReadOnlyList<ScreenInfo> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screens.Count == 0)
        {
            throw new ArgumentException("At least one screen is required.", nameof(screens));
        }

        ScreenInfo best = screens[0];
        long bestArea = -1;
        long bestDistance = long.MaxValue;
        long cx = lens.X + (lens.Width / 2);
        long cy = lens.Y + (lens.Height / 2);
        foreach (ScreenInfo s in screens)
        {
            long area = lens.IntersectionArea(s.Bounds);
            long dx = Math.Max(Math.Max(s.Bounds.X - cx, cx - s.Bounds.Right), 0);
            long dy = Math.Max(Math.Max(s.Bounds.Y - cy, cy - s.Bounds.Bottom), 0);
            long distance = (dx * dx) + (dy * dy);
            if (area > bestArea || (area == bestArea && area == 0 && distance < bestDistance))
            {
                best = s;
                bestArea = area;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Shrinks and shifts the lens so it lies inside the screen. Version 1 covers one screen at a time.</summary>
    public static PixelRect Fit(PixelRect lens, PixelRect screen)
    {
        int width = Math.Clamp(lens.Width, Math.Min(MinimumSize, screen.Width), screen.Width);
        int height = Math.Clamp(lens.Height, Math.Min(MinimumSize, screen.Height), screen.Height);
        int x = Math.Clamp(lens.X, screen.X, screen.Right - width);
        int y = Math.Clamp(lens.Y, screen.Y, screen.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    public static PixelRect Move(PixelRect lens, int dx, int dy, IReadOnlyList<ScreenInfo> screens)
    {
        var moved = lens with { X = lens.X + dx, Y = lens.Y + dy };
        return Fit(moved, ScreenFor(moved, screens).Bounds);
    }

    /// <summary>Grows or shrinks the lens from its right and bottom edges.</summary>
    public static PixelRect Resize(PixelRect lens, int dw, int dh, IReadOnlyList<ScreenInfo> screens)
    {
        PixelRect screen = ScreenFor(lens, screens).Bounds;
        int width = Math.Clamp(lens.Width + dw, MinimumSize, Math.Max(MinimumSize, screen.Right - lens.X));
        int height = Math.Clamp(lens.Height + dh, MinimumSize, Math.Max(MinimumSize, screen.Bottom - lens.Y));
        return Fit(lens with { Width = width, Height = height }, screen);
    }

    /// <summary>
    /// In dev mode the lens is limited to a quarter of the screen, so stopping at a breakpoint cannot leave a
    /// frozen rectangle over the editor (section 10.2).
    /// </summary>
    public static PixelRect LimitForDevMode(PixelRect lens, PixelRect screen)
    {
        int width = Math.Min(lens.Width, screen.Width / 2);
        int height = Math.Min(lens.Height, screen.Height / 2);
        return Fit(lens with { Width = width, Height = height }, screen);
    }

    /// <summary>A sensible first rectangle: the middle of the screen, half its width and height.</summary>
    public static PixelRect DefaultFor(PixelRect screen) =>
        new(screen.X + (screen.Width / 4), screen.Y + (screen.Height / 4), screen.Width / 2, screen.Height / 2);

    public static ScreenPosition PositionOf(ScreenInfo screen, IReadOnlyList<ScreenInfo> screens)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ScreenInfo? primary = screens.FirstOrDefault(static s => s.IsPrimary);
        if (primary is null || screen.IsPrimary)
        {
            return ScreenPosition.Primary;
        }

        int dx = (screen.Bounds.X + (screen.Bounds.Width / 2)) - (primary.Bounds.X + (primary.Bounds.Width / 2));
        int dy = (screen.Bounds.Y + (screen.Bounds.Height / 2)) - (primary.Bounds.Y + (primary.Bounds.Height / 2));
        if (Math.Abs(dx) >= Math.Abs(dy))
        {
            return dx < 0 ? ScreenPosition.Left : ScreenPosition.Right;
        }

        return dy < 0 ? ScreenPosition.Above : ScreenPosition.Below;
    }

    /// <summary>
    /// Identifies an arrangement of monitors, so the lens can remember its last rectangle and mode per layout.
    /// </summary>
    public static string LayoutKey(IReadOnlyList<ScreenInfo> screens) =>
        string.Join(
            "|",
            screens
                .OrderBy(static s => s.Bounds.X)
                .ThenBy(static s => s.Bounds.Y)
                .Select(static s => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}")));
}

/// <summary>The lens rectangle together with the "fill this screen" toggle and the rectangle to go back to.</summary>
public sealed record LensPlacement(PixelRect Window, bool FillsScreen)
{
    /// <summary>The rectangle the lens covers right now.</summary>
    public PixelRect Current(IReadOnlyList<ScreenInfo> screens) =>
        FillsScreen ? LensGeometry.ScreenFor(Window, screens).Bounds : LensGeometry.Fit(Window, LensGeometry.ScreenFor(Window, screens).Bounds);

    /// <summary>
    /// "Fill this screen" snaps the lens to the full bounds of the monitor it is mostly on, including the
    /// taskbar. The same command again restores the previous rectangle.
    /// </summary>
    public LensPlacement ToggleFill() => this with { FillsScreen = !FillsScreen };

    public LensPlacement MovedTo(PixelRect window) => new(window, false);
}
