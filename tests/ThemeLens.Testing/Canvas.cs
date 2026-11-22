// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;

namespace ThemeLens.Testing;

/// <summary>
/// A tiny software canvas for drawing fixtures: filled and rounded rectangles, anti-aliased lines, discs and
/// text. Blending is done on encoded values, as most desktop text rendering does, so anti-aliased edges contain
/// the same in-between colours a real screen would show.
/// </summary>
public sealed class Canvas
{
    public Canvas(int width, int height, Rgb8 background)
    {
        Image = ImageRgb8.Filled(width, height, background);
    }

    public ImageRgb8 Image { get; }

    public int Width => Image.Width;

    public int Height => Image.Height;

    public void Fill(int x, int y, int w, int h, Rgb8 colour)
    {
        for (int py = Math.Max(0, y); py < Math.Min(Height, y + h); py++)
        {
            for (int px = Math.Max(0, x); px < Math.Min(Width, x + w); px++)
            {
                Image[px, py] = colour;
            }
        }
    }

    /// <summary>A one-pixel outline just inside the rectangle.</summary>
    public void Outline(int x, int y, int w, int h, Rgb8 colour)
    {
        Fill(x, y, w, 1, colour);
        Fill(x, y + h - 1, w, 1, colour);
        Fill(x, y, 1, h, colour);
        Fill(x + w - 1, y, 1, h, colour);
    }

    public void RoundRect(float x, float y, float w, float h, float radius, Rgb8 colour)
    {
        radius = MathF.Min(radius, MathF.Min(w, h) / 2f);
        float cx = x + (w / 2f);
        float cy = y + (h / 2f);
        for (int py = (int)MathF.Floor(y) - 1; py <= (int)MathF.Ceiling(y + h); py++)
        {
            for (int px = (int)MathF.Floor(x) - 1; px <= (int)MathF.Ceiling(x + w); px++)
            {
                // Signed distance to a rounded box.
                float qx = MathF.Abs(px + 0.5f - cx) - ((w / 2f) - radius);
                float qy = MathF.Abs(py + 0.5f - cy) - ((h / 2f) - radius);
                float outside = MathF.Sqrt((MathF.Max(qx, 0) * MathF.Max(qx, 0)) + (MathF.Max(qy, 0) * MathF.Max(qy, 0)));
                float distance = outside + MathF.Min(MathF.Max(qx, qy), 0) - radius;
                Blend(px, py, colour, 0.5f - distance);
            }
        }
    }

    public void Disc(float cx, float cy, float radius, Rgb8 colour)
    {
        for (int py = (int)MathF.Floor(cy - radius) - 1; py <= (int)MathF.Ceiling(cy + radius); py++)
        {
            for (int px = (int)MathF.Floor(cx - radius) - 1; px <= (int)MathF.Ceiling(cx + radius); px++)
            {
                float dx = px + 0.5f - cx;
                float dy = py + 0.5f - cy;
                Blend(px, py, colour, 0.5f + radius - MathF.Sqrt((dx * dx) + (dy * dy)));
            }
        }
    }

    public void Line(float x0, float y0, float x1, float y1, float thickness, Rgb8 colour)
    {
        var layer = new Coverage(this, x0, y0, x1, y1, thickness);
        layer.Stroke(x0, y0, x1, y1, thickness);
        layer.Composite(colour);
    }

    /// <summary>Draws a connected line through the points, anti-aliased, without darkening the joints twice.</summary>
    public void Polyline(IReadOnlyList<(float X, float Y)> points, float thickness, Rgb8 colour)
    {
        if (points.Count < 2)
        {
            return;
        }

        var layer = new Coverage(
            this,
            points.Min(static p => p.X), points.Min(static p => p.Y),
            points.Max(static p => p.X), points.Max(static p => p.Y),
            thickness);
        for (int i = 1; i < points.Count; i++)
        {
            layer.Stroke(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y, thickness);
        }

        layer.Composite(colour);
    }

    /// <summary>
    /// Draws text with its left edge at <paramref name="x"/> and its baseline at <paramref name="baseline"/>.
    /// <paramref name="capHeight"/> is the height of a capital letter in pixels; <paramref name="weight"/> is the stroke width.
    /// Returns the x coordinate just after the text.
    /// </summary>
    public float Text(float x, float baseline, string text, float capHeight, float weight, Rgb8 colour)
    {
        float unit = capHeight / StrokeFont.CapHeight;
        float width = StrokeFont.Measure(text) * unit;
        var layer = new Coverage(this, x, baseline - (capHeight * 1.2f), x + width, baseline + (capHeight * 0.45f), weight);
        float pen = x;
        foreach (char c in text)
        {
            StrokeFont.Glyph glyph = StrokeFont.Get(c);
            foreach ((float X, float Y)[] stroke in glyph.Strokes)
            {
                for (int i = 1; i < stroke.Length; i++)
                {
                    layer.Stroke(
                        pen + (stroke[i - 1].X * unit), baseline - (stroke[i - 1].Y * unit),
                        pen + (stroke[i].X * unit), baseline - (stroke[i].Y * unit),
                        weight);
                }
            }

            pen += (glyph.Width + StrokeFont.Gap) * unit;
        }

        layer.Composite(colour);
        return x + width;
    }

    public float MeasureText(string text, float capHeight) => StrokeFont.Measure(text) * capHeight / StrokeFont.CapHeight;

    public void Blend(int x, int y, Rgb8 colour, float coverage)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height || coverage <= 0f)
        {
            return;
        }

        if (coverage >= 1f)
        {
            Image[x, y] = colour;
            return;
        }

        Rgb8 under = Image[x, y];
        Image[x, y] = new Rgb8(Mix(under.R, colour.R, coverage), Mix(under.G, colour.G, coverage), Mix(under.B, colour.B, coverage));
    }

    private static byte Mix(byte a, byte b, float t) => (byte)Math.Clamp((int)MathF.Round(a + ((b - a) * t)), 0, 255);

    /// <summary>A scratch layer that collects the coverage of several strokes (taking the maximum) before colouring.</summary>
    private sealed class Coverage
    {
        private readonly Canvas _canvas;
        private readonly int _x;
        private readonly int _y;
        private readonly int _w;
        private readonly int _h;
        private readonly float[] _cover;

        public Coverage(Canvas canvas, float x0, float y0, float x1, float y1, float thickness)
        {
            _canvas = canvas;
            int pad = (int)MathF.Ceiling(thickness) + 2;
            _x = (int)MathF.Floor(MathF.Min(x0, x1)) - pad;
            _y = (int)MathF.Floor(MathF.Min(y0, y1)) - pad;
            _w = (int)MathF.Ceiling(MathF.Max(x0, x1)) + pad - _x + 1;
            _h = (int)MathF.Ceiling(MathF.Max(y0, y1)) + pad - _y + 1;
            _cover = new float[_w * _h];
        }

        public void Stroke(float x0, float y0, float x1, float y1, float thickness)
        {
            float half = thickness / 2f;
            int minX = Math.Max(_x, (int)MathF.Floor(MathF.Min(x0, x1) - half - 1));
            int maxX = Math.Min(_x + _w - 1, (int)MathF.Ceiling(MathF.Max(x0, x1) + half + 1));
            int minY = Math.Max(_y, (int)MathF.Floor(MathF.Min(y0, y1) - half - 1));
            int maxY = Math.Min(_y + _h - 1, (int)MathF.Ceiling(MathF.Max(y0, y1) + half + 1));
            float dx = x1 - x0;
            float dy = y1 - y0;
            float lengthSquared = (dx * dx) + (dy * dy);
            for (int py = minY; py <= maxY; py++)
            {
                for (int px = minX; px <= maxX; px++)
                {
                    float cx = px + 0.5f - x0;
                    float cy = py + 0.5f - y0;
                    float t = lengthSquared <= 1e-6f ? 0f : Math.Clamp(((cx * dx) + (cy * dy)) / lengthSquared, 0f, 1f);
                    float ex = cx - (dx * t);
                    float ey = cy - (dy * t);
                    float cover = Math.Clamp(0.5f + half - MathF.Sqrt((ex * ex) + (ey * ey)), 0f, 1f);
                    int i = ((py - _y) * _w) + (px - _x);
                    _cover[i] = MathF.Max(_cover[i], cover);
                }
            }
        }

        public void Composite(Rgb8 colour)
        {
            for (int j = 0; j < _h; j++)
            {
                for (int i = 0; i < _w; i++)
                {
                    float cover = _cover[(j * _w) + i];
                    if (cover > 0f)
                    {
                        _canvas.Blend(_x + i, _y + j, colour, cover);
                    }
                }
            }
        }
    }
}
