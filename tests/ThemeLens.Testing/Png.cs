// SPDX-License-Identifier: GPL-3.0-or-later
using System.Buffers.Binary;
using System.IO.Compression;
using ThemeLens.Core.Imaging;

namespace ThemeLens.Testing;

/// <summary>
/// A minimal PNG reader and writer for fixtures and golden images: 8-bit RGB or RGBA, not interlaced.
/// It lives in the test helpers on purpose. The app itself must never write frames to disk (NFR-02).
/// </summary>
public static class Png
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Write(string path, ImageRgb8 image)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllBytes(path, Encode(image));
    }

    public static byte[] Encode(ImageRgb8 image)
    {
        ArgumentNullException.ThrowIfNull(image);
        using var output = new MemoryStream();
        output.Write(Signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], image.Height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // colour type: RGB
        header[10] = 0; // compression
        header[11] = 0; // filter method
        header[12] = 0; // not interlaced
        WriteChunk(output, "IHDR", header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            int stride = image.Width * 3;
            byte[] line = new byte[stride + 1];
            byte[] previous = new byte[stride];
            for (int y = 0; y < image.Height; y++)
            {
                // Filter 2 (Up) compresses flat user-interface pictures well and is trivial to get right.
                line[0] = 2;
                ReadOnlySpan<byte> row = image.Data.AsSpan(y * stride, stride);
                for (int i = 0; i < stride; i++)
                {
                    line[i + 1] = (byte)(row[i] - previous[i]);
                }

                zlib.Write(line);
                row.CopyTo(previous);
            }
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    public static ImageRgb8 Read(string path) => Decode(File.ReadAllBytes(path));

    public static ImageRgb8 Decode(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw new InvalidDataException("Not a PNG file.");
        }

        int width = 0;
        int height = 0;
        int channels = 0;
        using var data = new MemoryStream();
        int position = 8;
        while (position + 8 <= png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            string type = System.Text.Encoding.ASCII.GetString(png, position + 4, 4);
            ReadOnlySpan<byte> body = png.AsSpan(position + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(body);
                    height = BinaryPrimitives.ReadInt32BigEndian(body[4..]);
                    channels = body[9] switch
                    {
                        2 => 3,
                        6 => 4,
                        _ => throw new InvalidDataException("Only RGB and RGBA PNG files are supported."),
                    };
                    if (body[8] != 8 || body[12] != 0)
                    {
                        throw new InvalidDataException("Only 8-bit, non-interlaced PNG files are supported.");
                    }

                    break;
                case "IDAT":
                    data.Write(body);
                    break;
            }

            position += 12 + length;
        }

        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("The PNG file has no header.");
        }

        data.Position = 0;
        int stride = width * channels;
        byte[] raw = new byte[(stride + 1) * height];
        using (var zlib = new ZLibStream(data, CompressionMode.Decompress))
        {
            zlib.ReadExactly(raw);
        }

        var image = new ImageRgb8(width, height);
        byte[] current = new byte[stride];
        byte[] previous = new byte[stride];
        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            ReadOnlySpan<byte> line = raw.AsSpan((y * (stride + 1)) + 1, stride);
            for (int i = 0; i < stride; i++)
            {
                int left = i >= channels ? current[i - channels] : 0;
                int up = previous[i];
                int upLeft = i >= channels ? previous[i - channels] : 0;
                int predicted = filter switch
                {
                    0 => 0,
                    1 => left,
                    2 => up,
                    3 => (left + up) / 2,
                    4 => Paeth(left, up, upLeft),
                    _ => throw new InvalidDataException("Unknown PNG filter."),
                };
                current[i] = (byte)(line[i] + predicted);
            }

            for (int x = 0; x < width; x++)
            {
                image.Data[(((y * width) + x) * 3) + 0] = current[(x * channels) + 0];
                image.Data[(((y * width) + x) * 3) + 1] = current[(x * channels) + 1];
                image.Data[(((y * width) + x) * 3) + 2] = current[(x * channels) + 2];
            }

            (current, previous) = (previous, current);
        }

        return image;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> body)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, body.Length);
        output.Write(length);

        byte[] typed = new byte[4 + body.Length];
        System.Text.Encoding.ASCII.GetBytes(type, typed);
        body.CopyTo(typed.AsSpan(4));
        output.Write(typed);

        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc(typed));
        output.Write(crc);
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint c = 0xFFFFFFFFu;
        foreach (byte b in bytes)
        {
            c = CrcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        }

        return c ^ 0xFFFFFFFFu;
    }
}
