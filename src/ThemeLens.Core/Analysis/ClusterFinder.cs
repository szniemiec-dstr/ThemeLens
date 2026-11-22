// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Colour;
using ThemeLens.Core.Rendering;

namespace ThemeLens.Core.Analysis;

/// <summary>
/// Finds the main colour clusters under the lens (section 6.1): a histogram in OKLab picks the peaks, k-means
/// seeded from those peaks refines them, near-identical clusters are merged and at most eight are kept.
/// The whole analysis is deterministic: the same thumbnail always gives the same clusters.
/// </summary>
public static class ClusterFinder
{
    public const int MaxClusters = 8;

    private const int BinsL = 48;
    private const int BinsAb = 24;
    private const float AbExtent = 0.4f;

    /// <summary>Two histogram peaks closer than this (OKLab) are not both used as seeds.</summary>
    private const float SeedSpacing = 0.03f;

    /// <summary>Clusters closer than this are merged. Small enough to keep a white page and a light grey panel apart.</summary>
    private const float MergeDistance = 0.02f;

    /// <summary>From this chroma a colour counts as strongly coloured and survives even when it covers little area.</summary>
    public const float StrongChroma = 0.08f;

    private const float MinShare = 0.0015f;
    private const float MinStrongShare = 0.0004f;
    private const int MaxSeeds = 16;
    private const int Iterations = 8;

    /// <summary>A sample is "thin" when the 4×4 block around it has at least this luma range.</summary>
    private const float ThinRange = 0.10f;

    /// <summary>Radius, in lens pixels, of the neighbourhood that votes on whether a sample lies in a light or dark region.</summary>
    private const float RegionRadiusPixels = 24f;

    public static SceneAnalysis Analyse(Thumbnail thumbnail)
    {
        ArgumentNullException.ThrowIfNull(thumbnail);
        int n = thumbnail.Pixels.Length;
        if (n == 0)
        {
            return SceneAnalysis.Empty;
        }

        bool[] lightRegion = RegionOf(thumbnail, out float meanLuminance, out float medianLightness, out float lightFraction);

        // Collapse the samples to unique colours first: a typical screen has a few hundred.
        var index = new Dictionary<int, int>();
        var uniques = new List<Unique>();
        int borderSamples = 0;
        for (int y = 0; y < thumbnail.Height; y++)
        {
            for (int x = 0; x < thumbnail.Width; x++)
            {
                int i = (y * thumbnail.Width) + x;
                Rgb8 p = thumbnail.Pixels[i];
                int key = (p.R << 16) | (p.G << 8) | p.B;
                if (!index.TryGetValue(key, out int u))
                {
                    u = uniques.Count;
                    index.Add(key, u);
                    uniques.Add(new Unique(p, Oklab.FromRgb8(p)));
                }

                Unique unique = uniques[u];
                unique.Count++;
                if (thumbnail.Range[i] / 255f >= ThinRange)
                {
                    unique.Thin++;
                }

                if (lightRegion[i])
                {
                    unique.InLightRegion++;
                }

                if (x == 0 || y == 0 || x == thumbnail.Width - 1 || y == thumbnail.Height - 1)
                {
                    unique.OnBorder++;
                    borderSamples++;
                }
            }
        }

        List<Bin> bins = Histogram(uniques);
        List<Oklab> centres = Seeds(bins, n);
        centres = Refine(bins, centres);
        centres = Merge(bins, centres);

        // Drop the weakest clusters until at most eight remain. The histogram is enough to decide that, and is
        // far smaller than the list of unique colours when the screen shows a photo.
        while (true)
        {
            int weakest = Weakest(centres, bins, n);
            if (weakest < 0)
            {
                break;
            }

            centres.RemoveAt(weakest);
        }

        List<Accumulator> clusters = Label(uniques, centres);
        clusters.RemoveAll(static c => c.Count == 0);

        // The commonest flat colour of the light regions and of the dark regions: the backgrounds text sits on.
        Unique? lightBackground = null;
        Unique? darkBackground = null;
        foreach (Unique u in uniques)
        {
            if (u.Thin * 2 > u.Count)
            {
                continue;
            }

            if (u.InLightRegion * 2 >= u.Count)
            {
                lightBackground = lightBackground is null || u.Count > lightBackground.Count ? u : lightBackground;
            }
            else
            {
                darkBackground = darkBackground is null || u.Count > darkBackground.Count ? u : darkBackground;
            }
        }

        var result = new List<ColourCluster>(clusters.Count);
        foreach (Accumulator c in clusters)
        {
            Unique? background = c.InLightRegion * 2 >= c.Count ? lightBackground ?? darkBackground : darkBackground ?? lightBackground;
            Rgb8 colour = c.Representative(background?.Lab);
            result.Add(new ColourCluster
            {
                Colour = colour,
                Lab = Oklab.FromRgb8(colour),
                Luminance = Wcag.Luminance(colour),
                Share = (float)c.Count / n,
                LightRegionShare = (float)c.InLightRegion / c.Count,
                Thinness = (float)c.Thin / c.Count,
                EdgeTouch = borderSamples == 0 ? 0 : (float)c.OnBorder / borderSamples,
            });
        }

        result.Sort(static (a, b) =>
        {
            int byShare = b.Share.CompareTo(a.Share);
            return byShare != 0 ? byShare : string.CompareOrdinal(a.Colour.ToHex(), b.Colour.ToHex());
        });

        return new SceneAnalysis
        {
            Clusters = result,
            MeanLuminance = meanLuminance,
            MedianLightness = medianLightness,
            LightFraction = lightFraction,
        };
    }

    /// <summary>Decides for every sample whether it lies in a light or a dark region, the same way the renderer does.</summary>
    private static bool[] RegionOf(Thumbnail t, out float meanLuminance, out float medianLightness, out float wholeLens)
    {
        int w = t.Width;
        int h = t.Height;
        float[] luminance = new float[w * h];
        int[] lightnessHistogram = new int[256];
        double luminanceSum = 0;

        // Summed-area tables of the light and dark votes, one row and column larger than the thumbnail.
        double[] lightSat = new double[(w + 1) * (h + 1)];
        double[] darkSat = new double[(w + 1) * (h + 1)];
        for (int y = 0; y < h; y++)
        {
            double lightRow = 0;
            double darkRow = 0;
            for (int x = 0; x < w; x++)
            {
                float lum = Wcag.Luminance(t.Pixels[(y * w) + x]);
                luminance[(y * w) + x] = lum;
                luminanceSum += lum;
                lightnessHistogram[Math.Clamp((int)(MathF.Cbrt(lum) * 255f), 0, 255)]++;
                float flat = LensConstants.Flatness(t.Range[(y * w) + x] / 255f);
                lightRow += flat * LensConstants.LightVote(lum);
                darkRow += flat * LensConstants.DarkVote(lum);
                lightSat[((y + 1) * (w + 1)) + x + 1] = lightSat[(y * (w + 1)) + x + 1] + lightRow;
                darkSat[((y + 1) * (w + 1)) + x + 1] = darkSat[(y * (w + 1)) + x + 1] + darkRow;
            }
        }

        meanLuminance = (float)(luminanceSum / (w * h));
        int half = (w * h) / 2;
        int seen = 0;
        int median = 0;
        for (; median < 255; median++)
        {
            seen += lightnessHistogram[median];
            if (seen > half)
            {
                break;
            }
        }

        medianLightness = (median + 0.5f) / 255f;

        float allLight = (float)(lightSat[^1] / (w * h));
        float allDark = (float)(darkSat[^1] / (w * h));
        wholeLens = LensConstants.RefineLightFraction(0.5f, allLight, allDark);

        int radius = Math.Max(1, (int)MathF.Round(RegionRadiusPixels / t.SourcePixelsPerSample));
        int wideRadius = radius * LensConstants.BlockSize;
        bool[] light = new bool[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                float fraction = Refine(wholeLens, x, y, wideRadius);
                fraction = Refine(fraction, x, y, radius);
                light[(y * w) + x] = LensConstants.Polarity(fraction, luminance[(y * w) + x]) >= 0.5f;
            }
        }

        return light;

        float Refine(float wider, int x, int y, int r)
        {
            int x0 = Math.Max(0, x - r);
            int x1 = Math.Min(w, x + r + 1);
            int y0 = Math.Max(0, y - r);
            int y1 = Math.Min(h, y + r + 1);
            int area = (x1 - x0) * (y1 - y0);
            return LensConstants.RefineLightFraction(
                wider,
                (float)(Box(lightSat, x0, y0, x1, y1) / area),
                (float)(Box(darkSat, x0, y0, x1, y1) / area));
        }

        double Box(double[] sat, int x0, int y0, int x1, int y1) =>
            sat[(y1 * (w + 1)) + x1] - sat[(y0 * (w + 1)) + x1] - sat[(y1 * (w + 1)) + x0] + sat[(y0 * (w + 1)) + x0];
    }

    private static List<Bin> Histogram(List<Unique> uniques)
    {
        var map = new Dictionary<int, Bin>();
        foreach (Unique u in uniques)
        {
            // Greys have a and b of zero up to rounding noise, and that noise differs between operating systems.
            // The half-bin shift puts zero in the middle of a bin, so every grey lands in the same bin everywhere.
            int l = Math.Clamp((int)(u.Lab.L * BinsL), 0, BinsL - 1);
            int a = Math.Clamp((int)MathF.Floor(((u.Lab.A + AbExtent) / (2 * AbExtent) * BinsAb) + 0.5f), 0, BinsAb);
            int b = Math.Clamp((int)MathF.Floor(((u.Lab.B + AbExtent) / (2 * AbExtent) * BinsAb) + 0.5f), 0, BinsAb);
            int key = (((l * (BinsAb + 1)) + a) * (BinsAb + 1)) + b;
            if (!map.TryGetValue(key, out Bin? bin))
            {
                bin = new Bin(key);
                map.Add(key, bin);
            }

            bin.Add(u.Lab, u.Count);
        }

        var bins = map.Values.ToList();

        // A fixed order keeps everything downstream deterministic.
        bins.Sort(static (x, y) =>
        {
            int byCount = y.Count.CompareTo(x.Count);
            return byCount != 0 ? byCount : x.Key.CompareTo(y.Key);
        });
        return bins;
    }

    private static List<Oklab> Seeds(List<Bin> bins, int total)
    {
        var seeds = new List<Oklab>();
        int minCount = Math.Max(2, (int)(total * MinShare));
        int minStrongCount = Math.Max(2, (int)(total * MinStrongShare));

        // Peaks by size first, leaving room for small but strongly coloured ones.
        foreach (Bin bin in bins)
        {
            if (seeds.Count >= MaxSeeds - 4 || bin.Count < minCount)
            {
                break;
            }

            TryAddSeed(seeds, bin.Mean);
        }

        foreach (Bin bin in bins)
        {
            if (seeds.Count >= MaxSeeds)
            {
                break;
            }

            if (bin.Count >= minStrongCount && bin.Mean.Chroma >= StrongChroma)
            {
                TryAddSeed(seeds, bin.Mean);
            }
        }

        if (seeds.Count == 0 && bins.Count > 0)
        {
            seeds.Add(bins[0].Mean);
        }

        return seeds;
    }

    private static void TryAddSeed(List<Oklab> seeds, Oklab candidate)
    {
        foreach (Oklab s in seeds)
        {
            if (Oklab.Distance(s, candidate) < SeedSpacing)
            {
                return;
            }
        }

        seeds.Add(candidate);
    }

    private static List<Oklab> Refine(List<Bin> bins, List<Oklab> centres)
    {
        int k = centres.Count;
        double[] sumL = new double[k];
        double[] sumA = new double[k];
        double[] sumB = new double[k];
        long[] count = new long[k];

        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            Array.Clear(sumL);
            Array.Clear(sumA);
            Array.Clear(sumB);
            Array.Clear(count);
            foreach (Bin bin in bins)
            {
                int nearest = Nearest(centres, bin.Mean);
                sumL[nearest] += bin.SumL;
                sumA[nearest] += bin.SumA;
                sumB[nearest] += bin.SumB;
                count[nearest] += bin.Count;
            }

            bool moved = false;
            for (int c = 0; c < k; c++)
            {
                if (count[c] == 0)
                {
                    continue;
                }

                var updated = new Oklab((float)(sumL[c] / count[c]), (float)(sumA[c] / count[c]), (float)(sumB[c] / count[c]));
                if (Oklab.Distance(updated, centres[c]) > 1e-4f)
                {
                    moved = true;
                }

                centres[c] = updated;
            }

            if (!moved)
            {
                break;
            }
        }

        return centres;
    }

    private static List<Oklab> Merge(List<Bin> bins, List<Oklab> centres)
    {
        // Weight each centre by the samples it currently owns, so a merge lands near the bigger of the two.
        long[] weight = new long[centres.Count];
        foreach (Bin bin in bins)
        {
            weight[Nearest(centres, bin.Mean)] += bin.Count;
        }

        var items = new List<(Oklab Lab, long Weight)>();
        for (int i = 0; i < centres.Count; i++)
        {
            if (weight[i] > 0)
            {
                items.Add((centres[i], weight[i]));
            }
        }

        bool merged = true;
        while (merged)
        {
            merged = false;
            for (int i = 0; i < items.Count && !merged; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    if (Oklab.Distance(items[i].Lab, items[j].Lab) >= MergeDistance)
                    {
                        continue;
                    }

                    long w = items[i].Weight + items[j].Weight;
                    float t = (float)items[j].Weight / w;
                    Oklab a = items[i].Lab;
                    Oklab b = items[j].Lab;
                    items[i] = (new Oklab(a.L + ((b.L - a.L) * t), a.A + ((b.A - a.A) * t), a.B + ((b.B - a.B) * t)), w);
                    items.RemoveAt(j);
                    merged = true;
                    break;
                }
            }
        }

        return items.Select(static i => i.Lab).ToList();
    }

    private static List<Accumulator> Label(List<Unique> uniques, List<Oklab> centres)
    {
        var clusters = new List<Accumulator>(centres.Count);
        for (int i = 0; i < centres.Count; i++)
        {
            clusters.Add(new Accumulator());
        }

        foreach (Unique u in uniques)
        {
            clusters[Nearest(centres, u.Lab)].Add(u);
        }

        return clusters;
    }

    /// <summary>Returns the index of a cluster that should be dropped, or −1 when the set is acceptable.</summary>
    private static int Weakest(List<Oklab> centres, List<Bin> bins, int total)
    {
        if (centres.Count <= 1)
        {
            return -1;
        }

        long[] count = new long[centres.Count];
        double[] chromaSum = new double[centres.Count];
        foreach (Bin bin in bins)
        {
            int nearest = Nearest(centres, bin.Mean);
            count[nearest] += bin.Count;
            chromaSum[nearest] += bin.Mean.Chroma * bin.Count;
        }

        int weakestSmall = -1;
        float weakestSmallScore = float.MaxValue;
        int weakest = -1;
        float weakestScore = float.MaxValue;
        for (int i = 0; i < centres.Count; i++)
        {
            if (count[i] == 0)
            {
                return i;
            }

            float share = (float)count[i] / total;
            float chroma = (float)(chromaSum[i] / count[i]);
            bool strong = chroma >= StrongChroma;

            // Strongly coloured clusters are worth more than their area: links and status dots are small but matter.
            float score = share + (strong ? 0.02f * (chroma / 0.1f) : 0f);
            if (score < weakestScore)
            {
                weakestScore = score;
                weakest = i;
            }

            if (share < (strong ? MinStrongShare : MinShare) && score < weakestSmallScore)
            {
                weakestSmallScore = score;
                weakestSmall = i;
            }
        }

        if (weakestSmall >= 0)
        {
            return weakestSmall;
        }

        return centres.Count > MaxClusters ? weakest : -1;
    }

    private static int Nearest(List<Oklab> centres, Oklab lab)
    {
        int best = 0;
        float bestDistance = float.MaxValue;
        for (int c = 0; c < centres.Count; c++)
        {
            float dl = centres[c].L - lab.L;
            float da = centres[c].A - lab.A;
            float db = centres[c].B - lab.B;
            float d = (dl * dl) + (da * da) + (db * db);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = c;
            }
        }

        return best;
    }

    private sealed class Unique(Rgb8 colour, Oklab lab)
    {
        public Rgb8 Colour { get; } = colour;

        public Oklab Lab { get; } = lab;

        public int Count { get; set; }

        public int Thin { get; set; }

        public int InLightRegion { get; set; }

        public int OnBorder { get; set; }
    }

    private sealed class Bin(int key)
    {
        public int Key { get; } = key;

        public long Count { get; private set; }

        public double SumL { get; private set; }

        public double SumA { get; private set; }

        public double SumB { get; private set; }

        public Oklab Mean => new((float)(SumL / Count), (float)(SumA / Count), (float)(SumB / Count));

        public void Add(Oklab lab, int count)
        {
            Count += count;
            SumL += (double)lab.L * count;
            SumA += (double)lab.A * count;
            SumB += (double)lab.B * count;
        }
    }

    private sealed class Accumulator
    {
        private double _sumL;
        private double _sumA;
        private double _sumB;
        private readonly List<Unique> _members = [];
        private Unique? _mostCommon;

        public int Count { get; private set; }

        public int Thin { get; private set; }

        public int InLightRegion { get; private set; }

        public int OnBorder { get; private set; }

        public void Add(Unique u)
        {
            _members.Add(u);
            Count += u.Count;
            Thin += u.Thin;
            InLightRegion += u.InLightRegion;
            OnBorder += u.OnBorder;
            _sumL += (double)u.Lab.L * u.Count;
            _sumA += (double)u.Lab.A * u.Count;
            _sumB += (double)u.Lab.B * u.Count;
            if (_mostCommon is null
                || u.Count > _mostCommon.Count
                || (u.Count == _mostCommon.Count && string.CompareOrdinal(u.Colour.ToHex(), _mostCommon.Colour.ToHex()) < 0))
            {
                _mostCommon = u;
            }
        }

        public Oklab MeanLab() =>
            Count == 0 ? default : new Oklab((float)(_sumL / Count), (float)(_sumA / Count), (float)(_sumB / Count));

        /// <summary>
        /// Flat user-interface colours are exact, so the most common one is the true colour of the cluster and
        /// is not pulled towards its anti-aliased edges. Thin text is different: most of its pixels are edge
        /// blends, so the member furthest from the background it sits on is the true colour. Photos and
        /// gradients have no dominant colour; the mean stands in.
        /// </summary>
        public Rgb8 Representative(Oklab? background)
        {
            if (_mostCommon is null)
            {
                return Srgb.FromLinear(MeanLab().ToLinear());
            }

            if (background is { } bg && Thin * 10 >= Count * 6)
            {
                // Rank the members by distance from the background and take the one with a seventh of the
                // pixels beyond it: far enough out to be the core of the strokes, not so far that a handful of
                // stray pixels in another colour decide it.
                var ranked = new List<(float Distance, Unique Member)>(_members.Count);
                foreach (Unique u in _members)
                {
                    ranked.Add((Oklab.Distance(u.Lab, bg), u));
                }

                ranked.Sort(static (x, y) => y.Distance.CompareTo(x.Distance));
                int beyond = 0;
                foreach ((float _, Unique member) in ranked)
                {
                    beyond += member.Count;
                    if (beyond * 7 >= Count)
                    {
                        return member.Colour;
                    }
                }
            }

            return _mostCommon.Count >= Count * 0.10f ? _mostCommon.Colour : Srgb.FromLinear(MeanLab().ToLinear());
        }
    }
}
