// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;

namespace ThemeLens.Engine;

/// <summary>
/// The small sample document of the theme picker (figure 2): selecting a theme previews it here, inside the
/// window, before any lens appears on the desktop. The sample is drawn by this code, with bars standing in for
/// text as in the specification's own figures, and taken through the real engine, so the preview shows what the
/// lens would do and cannot drift from it.
/// </summary>
public static class ThemePreview
{
    public const int DefaultWidth = 320;
    public const int DefaultHeight = 200;

    private static readonly Rgb8 White = Rgb8.White;
    private static readonly Rgb8 Ink = Rgb8.FromHex("#222222");
    private static readonly Rgb8 Muted = Rgb8.FromHex("#8A8F98");
    private static readonly Rgb8 Panel = Rgb8.FromHex("#F1F3F5");
    private static readonly Rgb8 Blue = Rgb8.FromHex("#1A73E8");
    private static readonly Rgb8 Green = Rgb8.FromHex("#2E9E44");
    private static readonly Rgb8 Red = Rgb8.FromHex("#D93025");

    /// <summary>The sample as an ordinary bright app would draw it.</summary>
    public static ImageRgb8 Sample(int width = DefaultWidth, int height = DefaultHeight)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 160);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 100);
        var image = ImageRgb8.Filled(width, height, White);
        int margin = width / 16;
        int line = Math.Max(3, height / 50);
        int gap = line * 3;

        Fill(image, 0, 0, width, height / 9, Blue);                                   // title bar
        Fill(image, margin, height / 18 - line, width / 4, line * 2, White);          // its title

        int y = (height / 9) + gap + line;
        Fill(image, margin, y, width * 2 / 5, line * 2, Ink);                         // heading
        y += (line * 2) + gap;
        for (int i = 0; i < 3; i++)
        {
            Fill(image, margin, y, width - (2 * margin) - (i == 2 ? width / 4 : i * margin), line, Ink);
            y += line + gap - line;
        }

        Fill(image, margin, y, width / 4, line, Blue);                                // a link
        Fill(image, margin + (width / 4) + margin, y, width / 5, line, Muted);        // secondary text
        y += line + gap;

        int panelHeight = height - y - gap;
        Fill(image, margin, y, width - (2 * margin), panelHeight, Panel);             // a panel with two status rows
        int row = panelHeight / 2;
        int marker = Math.Max(8, Math.Min(row - (2 * line), 14));
        for (int i = 0; i < 2; i++)
        {
            int rowY = y + (i * row) + ((row - line) / 2);
            Fill(image, margin * 2, rowY, width / 3, line, Ink);
            Fill(image, width - (margin * 2) - marker, y + (i * row) + ((row - marker) / 2), marker, marker, i == 0 ? Green : Red);
        }

        return image;
    }

    /// <summary>The sample as the lens would show it in <paramref name="theme"/>.</summary>
    public static ImageRgb8 Render(Theme theme, int width = DefaultWidth, int height = DefaultHeight)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ImageRgb8 sample = Sample(width, height);
        SceneAnalysis scene = ClusterFinder.Analyse(Neighbourhood.Build(sample).ToThumbnail());
        LensTables tables = TableBuilder.Build(theme, RoleAssigner.Assign(scene), scene);
        return ReferenceRenderer.Render(sample, tables);
    }

    private static void Fill(ImageRgb8 image, int x, int y, int w, int h, Rgb8 colour)
    {
        for (int py = Math.Max(0, y); py < Math.Min(image.Height, y + h); py++)
        {
            for (int px = Math.Max(0, x); px < Math.Min(image.Width, x + w); px++)
            {
                image[px, py] = colour;
            }
        }
    }
}
