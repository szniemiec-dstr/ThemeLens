// SPDX-License-Identifier: GPL-3.0-or-later
namespace ThemeLens.Core.Mapping;

/// <summary>
/// A curve through a list of points, straight between them and flat beyond the ends. Built so that it never
/// reverses: that is what keeps anti-aliased text edges smooth after recolouring (section 6.3).
/// </summary>
public sealed class PiecewiseLinear
{
    private readonly float[] _x;
    private readonly float[] _y;

    private PiecewiseLinear(float[] x, float[] y)
    {
        _x = x;
        _y = y;
    }

    public int Count => _x.Length;

    /// <summary>
    /// Builds a non-decreasing curve. Points are sorted by x; a point that would make the curve go down, or that
    /// sits on top of the previous one, is dropped. List the points you care most about first: on a tie the
    /// earlier one wins.
    /// </summary>
    public static PiecewiseLinear NonDecreasing(IEnumerable<(float X, float Y)> points)
    {
        var ordered = points
            .Select(static (p, i) => (p.X, p.Y, Priority: i))
            .Where(static p => float.IsFinite(p.X) && float.IsFinite(p.Y))
            .OrderBy(static p => p.Priority)
            .ToList();

        // Insert in priority order, rejecting anything that conflicts with what is already there.
        var kept = new List<(float X, float Y)>();
        foreach ((float x, float y, _) in ordered)
        {
            bool ok = true;
            foreach ((float kx, float ky) in kept)
            {
                if (MathF.Abs(kx - x) < 1e-4f || (kx < x && ky > y) || (kx > x && ky < y))
                {
                    ok = false;
                    break;
                }
            }

            if (ok)
            {
                kept.Add((x, y));
            }
        }

        kept.Sort(static (a, b) => a.X.CompareTo(b.X));
        if (kept.Count == 0)
        {
            kept.Add((0f, 0f));
        }

        return new PiecewiseLinear(kept.Select(static p => p.X).ToArray(), kept.Select(static p => p.Y).ToArray());
    }

    public float Evaluate(float x)
    {
        if (x <= _x[0])
        {
            return _y[0];
        }

        int last = _x.Length - 1;
        if (x >= _x[last])
        {
            return _y[last];
        }

        int i = 1;
        while (_x[i] < x)
        {
            i++;
        }

        float t = (x - _x[i - 1]) / (_x[i] - _x[i - 1]);
        return _y[i - 1] + ((_y[i] - _y[i - 1]) * t);
    }
}
