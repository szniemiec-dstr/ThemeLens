// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Imaging;

/// <summary>A frame held in memory: 8-bit sRGB, three bytes per pixel, rows top to bottom.</summary>
public sealed class ImageRgb8
{
    public ImageRgb8(int width, int height)
        : this(width, height, new byte[checked(width * height * 3)])
    {
    }

    public ImageRgb8(int width, int height, byte[] data)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != width * height * 3)
        {
            throw new ArgumentException("The buffer must hold exactly width × height × 3 bytes.", nameof(data));
        }

        Width = width;
        Height = height;
        Data = data;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Data { get; }

    public int PixelCount => Width * Height;

    public Rgb8 this[int x, int y]
    {
        get
        {
            int i = ((y * Width) + x) * 3;
            return new Rgb8(Data[i], Data[i + 1], Data[i + 2]);
        }

        set
        {
            int i = ((y * Width) + x) * 3;
            Data[i] = value.R;
            Data[i + 1] = value.G;
            Data[i + 2] = value.B;
        }
    }

    /// <summary>Reads a pixel, clamping the coordinates to the image like a clamp-addressed texture.</summary>
    public Rgb8 Clamped(int x, int y) => this[Math.Clamp(x, 0, Width - 1), Math.Clamp(y, 0, Height - 1)];

    public static ImageRgb8 Filled(int width, int height, Rgb8 colour)
    {
        var image = new ImageRgb8(width, height);
        image.Fill(colour);
        return image;
    }

    public void Fill(Rgb8 colour)
    {
        for (int i = 0; i < Data.Length; i += 3)
        {
            Data[i] = colour.R;
            Data[i + 1] = colour.G;
            Data[i + 2] = colour.B;
        }
    }

    public ImageRgb8 Clone() => new(Width, Height, (byte[])Data.Clone());

    public bool ContentEquals(ImageRgb8 other) =>
        other.Width == Width && other.Height == Height && Data.AsSpan().SequenceEqual(other.Data);
}
