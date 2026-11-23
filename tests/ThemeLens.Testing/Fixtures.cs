// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;

namespace ThemeLens.Testing;

public readonly record struct Rect(int X, int Y, int Width, int Height);

/// <summary>A screen drawn for the tests, with the facts about it that scenarios refer to.</summary>
public sealed record Fixture(string FileName, ImageRgb8 Image, Rgb8 Page, Rgb8 Text, IReadOnlyDictionary<string, Rect> Regions);

/// <summary>
/// The fixture library (section 9.2). Every picture is drawn by the code below; no screenshots of third-party
/// apps are used. The PNG files in <c>tests/ThemeLens.Specs/Fixtures</c> are this code's output, committed so
/// that reviewers can look at them, and a test checks that the two have not drifted apart.
/// </summary>
public static class Fixtures
{
    public const int Width = 640;
    public const int Height = 400;

    private static readonly Rgb8 White = Rgb8.White;
    private static readonly Rgb8 Ink = Rgb8.FromHex("#222222");
    private static readonly Rgb8 Muted = Rgb8.FromHex("#8A8F98");
    private static readonly Rgb8 Grey = Rgb8.FromHex("#666666");
    private static readonly Rgb8 PanelLight = Rgb8.FromHex("#F1F3F5");
    private static readonly Rgb8 Blue = Rgb8.FromHex("#1A73E8");
    private static readonly Rgb8 Green = Rgb8.FromHex("#2E9E44");
    private static readonly Rgb8 Red = Rgb8.FromHex("#D93025");
    private static readonly Rgb8 Orange = Rgb8.FromHex("#F9A825");

    private static readonly string[] Words =
        ("the lens shows whatever is underneath in the colours you chose a black page with soft text high contrast " +
         "reds and greens you can tell apart or less blue in the evening clicks and typing go straight through to " +
         "the app below two suites ran overnight and every colour rule is checked against a library of screens")
        .Split(' ');

    private static readonly Dictionary<string, Func<Fixture>> Builders = new(StringComparer.Ordinal)
    {
        ["light-document.png"] = LightDocument,
        ["dark-editor.png"] = DarkEditor,
        ["light-spreadsheet.png"] = () => Spreadsheet(dark: false),
        ["dark-spreadsheet.png"] = () => Spreadsheet(dark: true),
        ["web-buttons.png"] = WebButtons,
        ["status-dashboard.png"] = StatusDashboard,
        ["chart.png"] = Chart,
        ["photo-on-white.png"] = PhotoOnWhite,
        ["gradient.png"] = Gradient,
        ["terminal.png"] = Terminal,
        ["mixed-halves.png"] = MixedHalves,
        ["dark-buttons.png"] = DarkButtons,
    };

    public static IReadOnlyCollection<string> Names => Builders.Keys;

    /// <summary>Draws a fixture from scratch.</summary>
    public static Fixture Draw(string fileName) =>
        Builders.TryGetValue(fileName, out Func<Fixture>? build)
            ? build()
            : throw new KeyNotFoundException($"There is no fixture called '{fileName}'.");

    /// <summary>
    /// One frame of a recording. "blinking-cursor-and-spinner" is the light document with a text cursor that
    /// blinks twice a second and a small spinner that turns once a second.
    /// </summary>
    public static ImageRgb8 RecordingFrame(string recording, double seconds)
    {
        if (recording != "blinking-cursor-and-spinner")
        {
            throw new KeyNotFoundException($"There is no recording called '{recording}'.");
        }

        var c = new Canvas(Width, Height, White);
        DrawDocument(c, out _);
        if ((int)(seconds * 2) % 2 == 0)
        {
            c.Fill(332, 96, 2, 14, Ink);
        }

        double angle = seconds * Math.PI * 2;
        var arc = new List<(float, float)>();
        for (int i = 0; i <= 12; i++)
        {
            double a = angle + (i * Math.PI * 1.4 / 12);
            arc.Add((600 + (float)(Math.Cos(a) * 9), 86 + (float)(Math.Sin(a) * 9)));
        }

        c.Polyline(arc, 2.4f, Blue);
        return c.Image;
    }

    private static Fixture LightDocument()
    {
        var c = new Canvas(Width, Height, White);
        DrawDocument(c, out Dictionary<string, Rect> regions);
        return new Fixture("light-document.png", c.Image, White, Ink, regions);
    }

    private static void DrawDocument(Canvas c, out Dictionary<string, Rect> regions)
    {
        c.Fill(0, 0, Width, 30, Blue);
        c.Text(14, 20, "Results.docx", 9, 1.6f, White);

        c.Text(24, 68, "Weekly test results", 15, 2.2f, Ink);
        float x = c.Text(24, 108, "Two suites ran overnight.", 8, 1.25f, Ink);
        float linkStart = x + 8;
        float linkEnd = c.Text(linkStart, 108, "Open the full log", 8, 1.25f, Blue);
        c.Fill((int)linkStart, 111, (int)(linkEnd - linkStart), 1, Blue);
        c.Text(linkEnd + 8, 108, "(updated 06:10)", 8, 1.25f, Muted);

        Paragraph(c, 24, 136, 592, 5, 8, 1.25f, 19, Ink, seed: 3);

        c.Fill(24, 244, 592, 76, PanelLight);
        c.Text(40, 270, "Colour mapping suite", 8, 1.25f, Ink);
        Pill(c, 520, 256, "Passed", Green);
        c.Text(40, 302, "Installer suite", 8, 1.25f, Ink);
        Pill(c, 520, 288, "Failed", Red);

        Paragraph(c, 24, 348, 592, 2, 8, 1.25f, 19, Grey, seed: 11);

        regions = new Dictionary<string, Rect>
        {
            ["title-bar"] = new(0, 0, Width, 30),
            ["panel"] = new(24, 244, 592, 76),
            ["passed"] = new(524, 258, 6, 16),
            ["failed"] = new(524, 290, 6, 16),
        };
    }

    private static Fixture DarkEditor()
    {
        Rgb8 background = Rgb8.FromHex("#1E1E1E");
        Rgb8 side = Rgb8.FromHex("#252526");
        Rgb8 tabs = Rgb8.FromHex("#2D2D2D");
        Rgb8 text = Rgb8.FromHex("#D4D4D4");
        Rgb8 lineNumber = Rgb8.FromHex("#858585");
        Rgb8 keyword = Rgb8.FromHex("#569CD6");
        Rgb8 str = Rgb8.FromHex("#CE9178");
        Rgb8 comment = Rgb8.FromHex("#6A9955");
        Rgb8 type = Rgb8.FromHex("#4EC9B0");

        var c = new Canvas(Width, Height, background);
        c.Fill(0, 0, Width, 28, tabs);
        c.Fill(150, 0, 130, 28, background);
        c.Text(162, 18, "LutBuilder.cs", 8, 1.2f, text);
        c.Text(296, 18, "Theme.cs", 8, 1.2f, lineNumber);

        c.Fill(0, 28, 150, Height - 50, side);
        string[] files = ["src", "Core", "Colour", "Oklab.cs", "Srgb.cs", "Mapping", "Lut3D.cs", "Themes", "tests"];
        for (int i = 0; i < files.Length; i++)
        {
            c.Text(14 + (i is 0 or 8 ? 0 : i is 1 or 2 or 5 or 7 ? 10 : 22), 52 + (i * 20), files[i], 8, 1.2f, i == 6 ? White : text);
        }

        (string Text, Rgb8 Colour)[][] lines =
        [
            [("// Builds the lookup table for one region.", comment)],
            [("public", keyword), ("static", keyword), ("Lut3D", type), ("Build(", text), ("Theme", type), ("theme)", text)],
            [("{", text)],
            [("    var", keyword), ("lut", text), ("=", text), ("new", keyword), ("Lut3D", type), ("();", text)],
            [("    foreach", keyword), ("(", text), ("var", keyword), ("node", text), ("in", keyword), ("Nodes)", text)],
            [("    {", text)],
            [("        var", keyword), ("name", text), ("=", text), ("\"soft-dark\"", str), (";", text)],
            [("        lut[node]", text), ("=", text), ("Map(node, theme);", text)],
            [("    }", text)],
            [("    return", keyword), ("lut;", text)],
            [("}", text)],
            [],
            [("// Limits are applied last.", comment)],
            [("private", keyword), ("const", keyword), ("float", keyword), ("MaxSlope", text), ("=", text), ("3.5f", str), (";", text)],
        ];
        for (int i = 0; i < lines.Length; i++)
        {
            int baseline = 52 + (i * 20);
            c.Text(162, baseline, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 8, 1.2f, lineNumber);
            float x = 196;
            foreach ((string word, Rgb8 colour) in lines[i])
            {
                int indent = word.Length - word.TrimStart().Length;
                x += indent * 7.4f;
                x = c.Text(x, baseline, word.TrimStart(), 8, 1.2f, colour) + 7;
            }
        }

        c.Fill(0, Height - 22, Width, 22, Rgb8.FromHex("#007ACC"));
        c.Text(12, Height - 7, "main   0 errors   UTF-8   C#", 7.5f, 1.2f, White);

        return new Fixture("dark-editor.png", c.Image, background, text, new Dictionary<string, Rect>
        {
            ["sidebar"] = new(0, 28, 150, Height - 50),
            ["status-bar"] = new(0, Height - 22, Width, 22),
        });
    }

    private static Fixture Spreadsheet(bool dark)
    {
        Rgb8 page = dark ? Rgb8.FromHex("#1F1F1F") : White;
        Rgb8 header = dark ? Rgb8.FromHex("#2B2B2B") : Rgb8.FromHex("#F3F4F6");
        Rgb8 grid = dark ? Rgb8.FromHex("#3A3A3A") : Rgb8.FromHex("#D0D7DE");
        Rgb8 text = dark ? Rgb8.FromHex("#E0E0E0") : Ink;
        Rgb8 headerText = dark ? Rgb8.FromHex("#A0A0A0") : Rgb8.FromHex("#555555");
        Rgb8 negative = dark ? Rgb8.FromHex("#FF6B6B") : Red;
        Rgb8 positive = dark ? Rgb8.FromHex("#5BD27A") : Rgb8.FromHex("#1E7B34");

        var c = new Canvas(Width, Height, page);
        const int rowHeight = 22;
        const int firstColumn = 40;
        const int columnWidth = 100;
        c.Fill(0, 0, Width, rowHeight, header);
        c.Fill(0, 0, firstColumn, Height, header);
        for (int col = 0; col < 6; col++)
        {
            c.Text(firstColumn + (col * columnWidth) + 44, 15, ((char)('A' + col)).ToString(), 8, 1.3f, headerText);
        }

        string[] labels = ["Region", "North", "South", "East", "West", "Central", "Coast", "Islands", "Online", "Total"];
        var random = new Random(7);
        for (int row = 1; row * rowHeight < Height; row++)
        {
            int baseline = (row * rowHeight) + 15;
            c.Text(12, baseline, row.ToString(System.Globalization.CultureInfo.InvariantCulture), 8, 1.3f, headerText);
            if (row - 1 >= labels.Length)
            {
                continue;
            }

            c.Text(firstColumn + 8, baseline, labels[row - 1], 8, row is 1 or 10 ? 1.7f : 1.25f, text);
            for (int col = 1; col < 6; col++)
            {
                if (row == 1)
                {
                    string title = col == 5 ? "Change" : "Q" + col;
                    c.Text(firstColumn + (col * columnWidth) + 8, baseline, title, 8, 1.7f, text);
                    continue;
                }

                int value = random.Next(-400, 2400);
                bool change = col == 5;
                string cell = change ? (value >= 0 ? "+" : "-") + (Math.Abs(value) / 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%" : Math.Abs(value).ToString(System.Globalization.CultureInfo.InvariantCulture);
                Rgb8 colour = change ? (value >= 0 ? positive : negative) : text;
                float width = c.MeasureText(cell, 8);
                c.Text(firstColumn + ((col + 1) * columnWidth) - 8 - width, baseline, cell, 8, 1.25f, colour);
            }
        }

        for (int y = rowHeight; y < Height; y += rowHeight)
        {
            c.Fill(0, y, Width, 1, grid);
        }

        for (int x = firstColumn; x < Width; x += columnWidth)
        {
            c.Fill(x, 0, 1, Height, grid);
        }

        // The selected cell.
        Rgb8 selection = dark ? Rgb8.FromHex("#4C8DF6") : Blue;
        c.Outline(firstColumn + (2 * columnWidth), 3 * rowHeight, columnWidth + 1, rowHeight + 1, selection);
        c.Outline(firstColumn + (2 * columnWidth) + 1, (3 * rowHeight) + 1, columnWidth - 1, rowHeight - 1, selection);

        return new Fixture(dark ? "dark-spreadsheet.png" : "light-spreadsheet.png", c.Image, page, text, new Dictionary<string, Rect>());
    }

    private static Fixture WebButtons()
    {
        Rgb8 page = Rgb8.FromHex("#FAFAFA");
        var c = new Canvas(Width, Height, page);
        c.Fill(0, 0, Width, 44, White);
        c.Fill(0, 44, Width, 1, Rgb8.FromHex("#E0E0E0"));
        c.Text(20, 28, "Acme Cloud", 10, 2f, Ink);
        c.Text(430, 27, "Docs", 8, 1.25f, Blue);
        c.Text(486, 27, "Pricing", 8, 1.25f, Blue);
        c.Text(558, 27, "Sign in", 8, 1.25f, Blue);

        c.Text(24, 88, "Deploy your project", 14, 2.1f, Ink);
        Paragraph(c, 24, 116, 380, 3, 8, 1.25f, 19, Grey, seed: 5);

        Button(c, 24, 186, 132, 36, "Deploy now", Blue, White);
        Button(c, 168, 186, 110, 36, "Approve", Green, White);
        Button(c, 290, 186, 100, 36, "Delete", Red, White);
        Button(c, 402, 186, 104, 36, "Review", Orange, Ink);
        c.RoundRect(518, 186, 98, 36, 6, Rgb8.FromHex("#C4C7CC"));
        c.RoundRect(519, 187, 96, 34, 5, page);
        c.Text(543, 209, "Cancel", 8, 1.4f, Ink);

        c.RoundRect(24, 246, 592, 130, 8, Rgb8.FromHex("#E0E0E0"));
        c.RoundRect(25, 247, 590, 128, 7, White);
        c.Text(44, 276, "Usage this month", 9, 1.7f, Ink);
        c.Text(44, 302, "Requests", 8, 1.25f, Grey);
        c.RoundRect(140, 292, 440, 12, 6, PanelLight);
        c.RoundRect(140, 292, 310, 12, 6, Blue);
        c.Text(44, 328, "Storage", 8, 1.25f, Grey);
        c.RoundRect(140, 318, 440, 12, 6, PanelLight);
        c.RoundRect(140, 318, 120, 12, 6, Green);
        c.Text(44, 354, "Errors", 8, 1.25f, Grey);
        c.RoundRect(140, 344, 440, 12, 6, PanelLight);
        c.RoundRect(140, 344, 36, 12, 6, Red);

        return new Fixture("web-buttons.png", c.Image, page, Ink, new Dictionary<string, Rect>
        {
            ["primary-button"] = new(24, 186, 132, 36),
        });
    }

    private static Fixture StatusDashboard()
    {
        var c = new Canvas(Width, Height, White);
        c.Text(24, 44, "Build status", 14, 2.1f, Ink);
        c.Text(24, 70, "Last 24 hours", 8, 1.25f, Muted);

        string[] services = ["Colour core", "Engine", "Capture", "Renderer", "Installer", "Website", "Specs", "UI tests", "Updater", "Themes"];
        bool[] ok = [true, true, false, true, false, true, true, false, true, true];
        var regions = new Dictionary<string, Rect>();
        for (int i = 0; i < services.Length; i++)
        {
            int column = i / 5;
            int row = i % 5;
            int x = 24 + (column * 304);
            int y = 96 + (row * 56);
            c.RoundRect(x, y, 288, 46, 6, PanelLight);

            // The marker: a solid square in the status colour. It is what the colour-vision scenarios measure.
            Rgb8 colour = ok[i] ? Green : Red;
            c.RoundRect(x + 16, y + 17, 12, 12, 3, colour);
            c.Text(x + 46, y + 28, services[i], 8, 1.4f, Ink);
            Pill(c, x + 200, y + 12, ok[i] ? "Passed" : "Failed", colour);

            string key = ok[i] ? "passed" : "failed";
            regions.TryAdd(key, new Rect(x + 19, y + 20, 6, 6));
        }

        return new Fixture("status-dashboard.png", c.Image, White, Ink, regions);
    }

    private static Fixture Chart()
    {
        var c = new Canvas(Width, Height, White);
        Rgb8 axis = Grey;
        Rgb8 gridLine = Rgb8.FromHex("#E5E7EB");
        Rgb8 purple = Rgb8.FromHex("#8E44AD");
        c.Text(24, 36, "Frames rendered per theme", 11, 1.9f, Ink);

        const int left = 60;
        const int bottom = 340;
        const int top = 70;
        for (int i = 0; i <= 5; i++)
        {
            int y = bottom - (i * 54);
            c.Fill(left, y, 540, 1, i == 0 ? axis : gridLine);
            string label = (i * 20).ToString(System.Globalization.CultureInfo.InvariantCulture);
            c.Text(left - 10 - c.MeasureText(label, 7), y + 4, label, 7, 1.2f, Grey);
        }

        c.Fill(left, top, 1, bottom - top, axis);

        Rgb8[] colours = [Blue, Orange, Green, Red, purple];
        int[] heights = [210, 150, 240, 90, 180];
        string[] names = ["Soft", "High", "Paper", "Warm", "Grey"];
        for (int i = 0; i < 5; i++)
        {
            int x = left + 36 + (i * 100);
            c.Fill(x, bottom - heights[i], 56, heights[i], colours[i]);
            c.Text(x + 28 - (c.MeasureText(names[i], 8) / 2), bottom + 22, names[i], 8, 1.25f, Ink);
        }

        var line = new List<(float, float)>();
        int[] trend = [120, 190, 160, 230, 205, 250];
        for (int i = 0; i < trend.Length; i++)
        {
            line.Add((left + 20 + (i * 100), bottom - trend[i]));
        }

        c.Polyline(line, 2.5f, Ink);
        foreach ((float px, float py) in line)
        {
            c.Disc(px, py, 4, Ink);
        }

        return new Fixture("chart.png", c.Image, White, Ink, new Dictionary<string, Rect>());
    }

    private static Fixture PhotoOnWhite()
    {
        var c = new Canvas(Width, Height, White);
        c.Text(24, 44, "Field notes", 14, 2.1f, Ink);
        Paragraph(c, 24, 76, 270, 12, 8, 1.25f, 19, Ink, seed: 9);

        // A stand-in for a photograph: soft shapes in many colours with a little grain. No real photo is used.
        var photo = new Rect(320, 60, 296, 222);
        var random = new Random(21);
        for (int y = 0; y < photo.Height; y++)
        {
            for (int x = 0; x < photo.Width; x++)
            {
                float u = (float)x / photo.Width;
                float v = (float)y / photo.Height;
                float sky = 1f - v;
                float sun = MathF.Exp(-(((u - 0.72f) * (u - 0.72f)) + ((v - 0.3f) * (v - 0.3f))) * 22f);
                float hill = Smooth.Step(0.02f, -0.02f, 0.62f + (0.08f * MathF.Sin(u * 7f)) - v);
                float grain = ((float)random.NextDouble() - 0.5f) * 0.03f;
                float r = Smooth.Lerp(0.55f + (0.35f * sky) + (0.5f * sun), 0.20f + (0.10f * u), hill) + grain;
                float g = Smooth.Lerp(0.70f + (0.20f * sky) + (0.35f * sun), 0.45f + (0.20f * MathF.Sin((u * 5f) + (v * 9f))), hill) + grain;
                float b = Smooth.Lerp(0.92f - (0.15f * v) + (0.1f * sun), 0.18f + (0.08f * v), hill) + grain;
                c.Image[photo.X + x, photo.Y + y] = new Rgb8(ToByte(r), ToByte(g), ToByte(b));
            }
        }

        c.Text(320, 304, "Morning over the valley", 8, 1.25f, Grey);
        Paragraph(c, 24, 332, 592, 3, 8, 1.25f, 19, Ink, seed: 13);

        return new Fixture("photo-on-white.png", c.Image, White, Ink, new Dictionary<string, Rect> { ["photo"] = photo });
    }

    private static Fixture Gradient()
    {
        var c = new Canvas(Width, Height, White);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                float u = (float)x / (Width - 1);
                if (y < 80)
                {
                    // A grey ramp from black to white.
                    byte v = ToByte(u);
                    c.Image[x, y] = new Rgb8(v, v, v);
                }
                else
                {
                    // Every hue, from pale at the top to deep at the bottom.
                    float lightness = Smooth.Lerp(0.95f, 0.30f, (float)(y - 80) / (Height - 81));
                    float hue = u * MathF.PI * 2f;
                    var lab = new Oklab(lightness, 0.13f * MathF.Cos(hue), 0.13f * MathF.Sin(hue));
                    c.Image[x, y] = Srgb.FromLinear(Gamut.Fit(lab.ToLinear()));
                }
            }
        }

        return new Fixture("gradient.png", c.Image, White, Ink, new Dictionary<string, Rect>());
    }

    private static Fixture Terminal()
    {
        Rgb8 background = Rgb8.FromHex("#0C0C0C");
        Rgb8 text = Rgb8.FromHex("#CCCCCC");
        Rgb8 prompt = Rgb8.FromHex("#16C60C");
        Rgb8 error = Rgb8.FromHex("#E74856");
        Rgb8 warning = Rgb8.FromHex("#F9F1A5");
        Rgb8 path = Rgb8.FromHex("#3B78FF");

        var c = new Canvas(Width, Height, background);
        (string Text, Rgb8 Colour)[][] lines =
        [
            [("user@box", prompt), ("~/themelens", path), ("$ dotnet test", text)],
            [("Running tests from ThemeLens.Core.Tests.dll", text)],
            [("  passed  Oklab round trip", text)],
            [("  passed  Lightness curve is monotonic", text)],
            [("  failed  Shader matches reference renderer", error)],
            [("    expected 0 differing pixels but found 12", error)],
            [("warning: 2 scenarios skipped on this platform", warning)],
            [("Test run summary: Failed!", error)],
            [("  total: 214   failed: 1   succeeded: 211   skipped: 2", text)],
            [],
            [("user@box", prompt), ("~/themelens", path), ("$ git status", text)],
            [("On branch main", text)],
            [("nothing to commit, working tree clean", text)],
            [("user@box", prompt), ("~/themelens", path), ("$", text)],
        ];
        for (int i = 0; i < lines.Length; i++)
        {
            float x = 14;
            foreach ((string word, Rgb8 colour) in lines[i])
            {
                int indent = word.Length - word.TrimStart().Length;
                x += indent * 7.4f;
                x = c.Text(x, 28 + (i * 22), word.TrimStart(), 8, 1.25f, colour) + 8;
            }

            if (i == lines.Length - 1)
            {
                // The cursor block.
                c.Fill((int)x, 28 + (i * 22) - 12, 8, 15, text);
            }
        }

        return new Fixture("terminal.png", c.Image, background, text, new Dictionary<string, Rect>());
    }

    private static Fixture MixedHalves()
    {
        ImageRgb8 light = LightDocument().Image;
        ImageRgb8 dark = DarkEditor().Image;
        var image = new ImageRgb8(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                image[x, y] = x < Width / 2 ? light[x, y] : dark[x - (Width / 2) + 150, y];
            }
        }

        return new Fixture("mixed-halves.png", image, White, Ink, new Dictionary<string, Rect>
        {
            ["light-half"] = new(0, 0, Width / 2, Height),
            ["dark-half"] = new(Width / 2, 0, Width / 2, Height),
        });
    }

    /// <summary>
    /// A light page with dark shapes on it that are not text: buttons, chips, tiles in a grid, a toolbar with
    /// light controls. The shapes are regions of their own; the gaps between them are page.
    /// </summary>
    private static Fixture DarkButtons()
    {
        Rgb8 black = Rgb8.FromHex("#111111");
        Rgb8 night = Rgb8.FromHex("#1E1E1E");
        Rgb8 soft = Rgb8.FromHex("#D4D4D4");
        var c = new Canvas(Width, Height, White);
        c.Text(24, 36, "Choose a plan", 14, 2.1f, Ink);
        Paragraph(c, 24, 62, 400, 2, 8, 1.25f, 19, Grey, seed: 21);

        // Buttons in a row, 8 pixels apart.
        string[] plans = ["Starter", "Team", "Business", "Talk to us"];
        int x = 24;
        foreach (string plan in plans)
        {
            int width = (int)c.MeasureText(plan, 8) + 40;
            c.RoundRect(x, 104, width, 34, 6, black);
            c.Text(x + 20, 126, plan, 8, 1.5f, White);
            x += width + 8;
        }

        // Chips, small icons and a thick rule: the icons and the rule are content, the chips are not.
        x = 24;
        foreach (string tag in (string[])["monthly", "yearly", "students", "open source"])
        {
            int width = (int)c.MeasureText(tag, 7) + 22;
            c.RoundRect(x, 152, width, 22, 11, night);
            c.Text(x + 11, 167, tag, 7, 1.3f, soft);
            x += width + 6;
        }

        c.Fill(x + 16, 156, 14, 14, night);
        c.Disc(x + 52, 163, 6, night);
        c.Fill(x + 70, 160, 120, 6, night);

        // Tiles in a grid with 10-pixel gutters.
        string[] captions = ["Colour", "Engine", "Capture", "Themes", "Updates", "Tests"];
        for (int i = 0; i < captions.Length; i++)
        {
            int tx = 24 + ((i % 3) * 126);
            int ty = 190 + ((i / 3) * 74);
            c.Fill(tx, ty, 116, 64, night);
            c.Text(tx + 12, ty + 24, captions[i], 8, 1.5f, White);
            c.Text(tx + 12, ty + 46, "ready", 7, 1.2f, soft);
        }

        c.Text(420, 210, "Tiles sit close together.", 8, 1.25f, Ink);
        c.Text(420, 232, "The gaps between them", 8, 1.25f, Ink);
        c.Text(420, 254, "are still the page.", 8, 1.25f, Ink);

        // A dark toolbar with light controls on it.
        c.Fill(0, 352, Width, 48, night);
        c.Text(20, 381, "Toolbar", 8, 1.5f, soft);
        c.RoundRect(110, 362, 180, 28, 4, White);
        c.Text(122, 381, "Search", 8, 1.25f, Grey);
        c.Fill(310, 373, 120, 6, soft);
        c.Disc(380, 376, 7, White);
        c.Fill(600, 362, 10, 28, soft);

        return new Fixture("dark-buttons.png", c.Image, White, Ink, new Dictionary<string, Rect>
        {
            ["first-button"] = new(24, 104, (int)c.MeasureText(plans[0], 8) + 40, 34),
            ["gap-between-buttons"] = new(24 + (int)c.MeasureText(plans[0], 8) + 41, 110, 6, 22),
            ["tile-gutter"] = new(141, 196, 8, 52),
            ["toolbar-label"] = new(12, 364, 90, 26),
            ["slider"] = new(310, 369, 120, 14),
        });
    }

    private static void Paragraph(Canvas c, int x, int baseline, int width, int lines, float capHeight, float weight, int leading, Rgb8 colour, int seed)
    {
        var random = new Random(seed);
        float space = c.MeasureText("n", capHeight) * 0.9f;
        for (int line = 0; line < lines; line++)
        {
            float pen = x;
            bool last = line == lines - 1;
            float limit = x + (last ? width * 0.6f : width);
            while (true)
            {
                string word = Words[random.Next(Words.Length)];
                float w = c.MeasureText(word, capHeight);
                if (pen + w > limit)
                {
                    break;
                }

                c.Text(pen, baseline + (line * leading), word, capHeight, weight, colour);
                pen += w + space;
            }
        }
    }

    private static void Pill(Canvas c, int x, int y, string label, Rgb8 colour)
    {
        float width = c.MeasureText(label, 7.5f) + 22;
        c.RoundRect(x, y, width, 22, 11, colour);
        c.Text(x + 11, y + 15, label, 7.5f, 1.5f, White);
    }

    private static void Button(Canvas c, int x, int y, int w, int h, string label, Rgb8 fill, Rgb8 text)
    {
        c.RoundRect(x, y, w, h, 6, fill);
        float width = c.MeasureText(label, 8);
        c.Text(x + ((w - width) / 2f), y + (h / 2f) + 4, label, 8, 1.5f, text);
    }

    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}
