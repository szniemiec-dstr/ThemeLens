// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// A synthetic screen for <c>--source pattern</c>: a light page and a dark page side by side, each with
/// text-like bars, a row of coloured blocks, and a strip in the middle where something moves. It gives every
/// part of the lens something to do without capturing anything.
/// </summary>
/// <remarks>The picture is drawn into a B8G8R8A8 buffer, the layout of a captured frame.</remarks>
internal static class TestPattern
{
    private static readonly Rgb8 White = Rgb8.White;
    private static readonly Rgb8 Ink = Rgb8.FromHex("#222222");
    private static readonly Rgb8 Grey = Rgb8.FromHex("#666666");
    private static readonly Rgb8 PanelLight = Rgb8.FromHex("#F1F3F5");
    private static readonly Rgb8 Blue = Rgb8.FromHex("#1A73E8");
    private static readonly Rgb8 Green = Rgb8.FromHex("#2E9E44");
    private static readonly Rgb8 Red = Rgb8.FromHex("#D93025");
    private static readonly Rgb8 Orange = Rgb8.FromHex("#F9A825");
    private static readonly Rgb8 DarkPage = Rgb8.FromHex("#1E1E1E");
    private static readonly Rgb8 DarkPanel = Rgb8.FromHex("#252526");
    private static readonly Rgb8 DarkText = Rgb8.FromHex("#D4D4D4");
    private static readonly Rgb8 Keyword = Rgb8.FromHex("#569CD6");
    private static readonly Rgb8 Literal = Rgb8.FromHex("#CE9178");
    private static readonly Rgb8 Comment = Rgb8.FromHex("#6A9955");

    private static readonly Rgb8[] Blocks =
    [
        Red, Orange, Rgb8.FromHex("#FDD835"), Green, Rgb8.FromHex("#00ACC1"), Blue, Rgb8.FromHex("#8E24AA"),
        Rgb8.Black, Rgb8.FromHex("#404040"), Rgb8.FromHex("#808080"), Rgb8.FromHex("#C0C0C0"), White,
    ];

    /// <summary>The rows the moving strip covers. Only these change from frame to frame.</summary>
    public static (int Top, int Bottom) MovingRows(int height)
    {
        int unit = Unit(height);
        int top = (height / 2) - (6 * unit);
        return (Math.Max(0, top), Math.Min(height, top + (12 * unit)));
    }

    /// <summary>Draws everything that stands still.</summary>
    public static void DrawBackground(byte[] bgra, int width, int height)
    {
        int unit = Unit(height);
        int half = width / 2;

        // Left: a light document.
        Fill(bgra, width, height, 0, 0, half, height, White);
        Fill(bgra, width, height, 0, 0, half, 10 * unit, Blue);
        int margin = 8 * unit;
        Fill(bgra, width, height, margin, 16 * unit, half / 3, 4 * unit, Ink);
        DrawLines(bgra, width, height, margin, 26 * unit, half - (2 * margin), 12, unit, Grey, Blue, seed: 1);
        int panelTop = 150 * unit;
        Fill(bgra, width, height, margin, panelTop, half - (2 * margin), 40 * unit, PanelLight);
        DrawLines(bgra, width, height, margin + (4 * unit), panelTop + (6 * unit), (half / 2) - margin, 5, unit, Grey, Grey, seed: 2);
        Fill(bgra, width, height, half - margin - (24 * unit), panelTop + (6 * unit), 20 * unit, 8 * unit, Green);
        Fill(bgra, width, height, half - margin - (24 * unit), panelTop + (22 * unit), 20 * unit, 8 * unit, Red);

        // Right: a dark editor.
        Fill(bgra, width, height, half, 0, width - half, height, DarkPage);
        Fill(bgra, width, height, half, 0, 40 * unit, height, DarkPanel);
        DrawLines(bgra, width, height, half + (4 * unit), 8 * unit, 30 * unit, 14, unit, DarkText, DarkText, seed: 3);
        int codeLeft = half + (48 * unit);
        int codeWidth = width - codeLeft - margin;
        for (int row = 0; row < 16; row++)
        {
            int y = (8 + (row * 6)) * unit;
            int x = codeLeft + ((row % 4) * 6 * unit);
            int first = 10 + (Hash(row, 4) % 14);
            int second = 8 + (Hash(row, 5) % 30);
            int third = 6 + (Hash(row, 6) % 20);
            Fill(bgra, width, height, x, y, Math.Min(first * unit, codeWidth), 2 * unit, row % 5 == 0 ? Comment : Keyword);
            Fill(bgra, width, height, x + ((first + 2) * unit), y, Math.Min(second * unit, codeWidth), 2 * unit, DarkText);
            if (row % 3 == 1)
            {
                Fill(bgra, width, height, x + ((first + second + 4) * unit), y, Math.Min(third * unit, codeWidth), 2 * unit, Literal);
            }
        }

        // Bottom: coloured blocks and a grey ramp, across both pages.
        int stripTop = height - (36 * unit);
        int blockWidth = Math.Max(1, (width - (2 * margin)) / Blocks.Length);
        for (int i = 0; i < Blocks.Length; i++)
        {
            Fill(bgra, width, height, margin + (i * blockWidth), stripTop, blockWidth - unit, 24 * unit, Blocks[i]);
        }

        DrawMovingRows(bgra, width, height, 0.0);
    }

    /// <summary>Draws the strip in the middle for a moment in time: a block that slides to and fro, and a blinking cursor on each page.</summary>
    public static void DrawMovingRows(byte[] bgra, int width, int height, double seconds)
    {
        int unit = Unit(height);
        int half = width / 2;
        (int top, int bottom) = MovingRows(height);
        Fill(bgra, width, height, 0, top, half, bottom - top, White);
        Fill(bgra, width, height, half, top, width - half, bottom - top, DarkPage);
        Fill(bgra, width, height, half, top, 40 * unit, bottom - top, DarkPanel);

        // A block that takes eight seconds to cross the screen and come back.
        int block = 16 * unit;
        double phase = (seconds / 8.0) % 1.0;
        double position = phase < 0.5 ? phase * 2.0 : 2.0 - (phase * 2.0);
        int x = (int)Math.Round(position * Math.Max(0, width - block));
        Fill(bgra, width, height, x, top + (2 * unit), block, 8 * unit, Orange);

        // Text cursors that blink twice a second.
        if ((long)Math.Floor(seconds * 2.0) % 2 == 0)
        {
            Fill(bgra, width, height, 8 * unit, top + (2 * unit), unit, 8 * unit, Ink);
            Fill(bgra, width, height, half + (48 * unit), top + (2 * unit), unit, 8 * unit, DarkText);
        }
    }

    private static int Unit(int height) => Math.Max(1, height / 270);

    private static int Hash(int a, int b) => (int)(((uint)((a * 73856093) ^ (b * 19349663))) % 9973);

    private static void DrawLines(byte[] bgra, int width, int height, int x, int y, int maxWidth, int count, int unit, Rgb8 colour, Rgb8 accent, int seed)
    {
        for (int i = 0; i < count; i++)
        {
            int length = maxWidth * (55 + (Hash(i, seed) % 45)) / 100;
            Fill(bgra, width, height, x, y + (i * 6 * unit), length, 2 * unit, i % 7 == 5 ? accent : colour);
        }
    }

    private static void Fill(byte[] bgra, int width, int height, int x, int y, int w, int h, Rgb8 colour)
    {
        int left = Math.Clamp(x, 0, width);
        int right = Math.Clamp(x + w, 0, width);
        int top = Math.Clamp(y, 0, height);
        int bottom = Math.Clamp(y + h, 0, height);
        for (int row = top; row < bottom; row++)
        {
            int i = ((row * width) + left) * 4;
            for (int column = left; column < right; column++, i += 4)
            {
                bgra[i] = colour.B;
                bgra[i + 1] = colour.G;
                bgra[i + 2] = colour.R;
                bgra[i + 3] = 255;
            }
        }
    }
}
