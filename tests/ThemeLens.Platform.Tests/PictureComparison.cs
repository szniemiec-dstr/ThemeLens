// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Text;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;

namespace ThemeLens.Platform.Tests;

/// <summary>
/// How far a picture from the GPU is from the picture the reference renderer makes. The verdict comes with
/// everything needed to find the cause from a build log alone: counts, where the differing pixels are, how big
/// the differences are and what the worst ones look like. No picture is ever written anywhere.
/// </summary>
internal sealed class PictureComparison
{
    /// <summary>A channel may differ by this much (of 255) without counting: rounding between passes.</summary>
    public const int Near = 2;

    /// <summary>No channel of any pixel may differ by more than this.</summary>
    public const int Far = 8;

    /// <summary>
    /// At least this share of the pixels must be within <see cref="Near"/>. The rest is what the texture
    /// sampler costs: it reads the lookup table at one of 256 positions between two entries, where the
    /// reference interpolates exactly, and strong sharpening multiplies the difference. The worst case seen is
    /// a full-colour gradient with local contrast and strong sharpen both on: 0.53% of pixels off by 3 to
    /// 5 of 255. A mistake in the shader shows as thousands of pixels far beyond <see cref="Far"/>.
    /// </summary>
    public const double RequiredNearShare = 0.99;

    private const int SamplesToShow = 6;

    private readonly int[] _bySize = new int[256];
    private readonly List<string> _samples = [];
    private string? _sizeMismatch;
    private int _pixels;
    private int _overNear;
    private int _overFar;
    private int _worst;
    private int _worstX;
    private int _worstY;
    private Rgb8 _worstExpected;
    private Rgb8 _worstActual;
    private int _left = int.MaxValue;
    private int _top = int.MaxValue;
    private int _right = -1;
    private int _bottom = -1;

    private PictureComparison()
    {
    }

    public bool Passes => _sizeMismatch is null && _overFar == 0 && _overNear <= _pixels * (1.0 - RequiredNearShare);

    public bool IsIdentical => _sizeMismatch is null && _bySize[0] == _pixels;

    public static PictureComparison Compare(ImageRgb8 expected, ImageRgb8 actual)
    {
        var result = new PictureComparison();
        if (expected.Width != actual.Width || expected.Height != actual.Height)
        {
            result._sizeMismatch = $"the reference is {expected.Width}×{expected.Height} but the shader drew {actual.Width}×{actual.Height}";
            return result;
        }

        result._pixels = expected.PixelCount;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                Rgb8 e = expected[x, y];
                Rgb8 a = actual[x, y];
                int difference = e.MaxChannelDifference(a);
                result._bySize[difference]++;
                if (difference > result._worst)
                {
                    result._worst = difference;
                    result._worstX = x;
                    result._worstY = y;
                    result._worstExpected = e;
                    result._worstActual = a;
                }

                if (difference <= Near)
                {
                    continue;
                }

                result._overNear++;
                result._left = Math.Min(result._left, x);
                result._top = Math.Min(result._top, y);
                result._right = Math.Max(result._right, x);
                result._bottom = Math.Max(result._bottom, y);
                if (difference > Far)
                {
                    result._overFar++;
                }

                if (result._samples.Count < SamplesToShow)
                {
                    result._samples.Add($"({x}, {y}) expected {e} got {a}");
                }
            }
        }

        return result;
    }

    public string Describe()
    {
        if (_sizeMismatch is not null)
        {
            return _sizeMismatch + ".";
        }

        var text = new StringBuilder();
        double share = _pixels == 0 ? 0 : 100.0 * _overNear / _pixels;
        text.Append(CultureInfo.InvariantCulture, $"{_overNear} of {_pixels} pixels ({share:0.###}%) differ by more than {Near}/255");
        text.Append(CultureInfo.InvariantCulture, $" (allowed: {100.0 * (1.0 - RequiredNearShare):0.#}%), {_overFar} of them by more than {Far}/255 (allowed: none). ");
        if (_overNear > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"They lie within x {_left}..{_right}, y {_top}..{_bottom}. ");
        }

        text.Append(CultureInfo.InvariantCulture, $"Worst pixel: {_worst}/255 at ({_worstX}, {_worstY}), expected {_worstExpected}, got {_worstActual}. ");
        text.Append("Pixels by largest channel difference:");
        for (int size = 0; size <= Far; size++)
        {
            text.Append(CultureInfo.InvariantCulture, $" {size}: {_bySize[size]},");
        }

        int beyond = 0;
        for (int size = Far + 1; size < _bySize.Length; size++)
        {
            beyond += _bySize[size];
        }

        text.Append(CultureInfo.InvariantCulture, $" more: {beyond}.");
        if (_samples.Count > 0)
        {
            text.Append(" First differing pixels: ").AppendJoin("; ", _samples).Append('.');
        }

        return text.ToString();
    }
}
