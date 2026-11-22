// SPDX-License-Identifier: GPL-3.0-or-later
using System.Numerics;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Themes;

namespace ThemeLens.Core.Mapping;

/// <summary>
/// Builds the lookup tables (section 6.3). The work happens off the render thread, a few times a second at most.
/// </summary>
/// <remarks>
/// <para>
/// The lightness rule is stated in contrast, not in lightness: a source colour that has contrast c against the
/// source page is shown with contrast f(c) against the theme's page, where f is one non-decreasing curve through
/// the anchor pairs (source Page to theme Page, source Panel to theme Panel, source Text to theme Text). Which
/// side of the page the text sits on comes from the theme, so the same rule turns a light document dark and
/// leaves a dark editor dark. Because the curve never reverses, anti-aliased text edges stay smooth.
/// </para>
/// <para>
/// Hue and chroma: near-grey inputs take the theme's neutral tint. Coloured inputs keep their hue, and are pulled
/// towards the theme colour of a nearby accent cluster with a weight that falls off with distance, so in-between
/// colours blend. Limits come last: maximum chroma, maximum luminance, then warmth, after which text-like
/// colours are corrected to hold the contrast they had before warming.
/// </para>
/// </remarks>
public static class TableBuilder
{
    /// <summary>How much a colour's chroma counts as contrast, so a saturated highlight does not vanish into the page.</summary>
    private const float ChromaAsContrast = 2.0f;

    /// <summary>Below the first chroma an input is treated as grey; above the second it is fully "coloured".</summary>
    private const float GreyChroma = 0.02f;
    private const float ColouredChroma = 0.07f;

    /// <summary>Reach of an accent anchor in colour distance. At this distance the pull has dropped to about a third.</summary>
    private const float AccentReach = 0.07f;

    /// <summary>The curve never amplifies contrast by more than this factor (in log contrast), whatever the anchors say.</summary>
    private const float MaxSlope = 3.5f;

    /// <summary>Source page and text luminance assumed for a region nothing is known about yet.</summary>
    private const float DefaultLightPage = 0.85f;
    private const float DefaultLightText = 0.02f;
    private const float DefaultDarkPage = 0.02f;
    private const float DefaultDarkText = 0.80f;

    /// <summary>Red–green assist: how far the red–green axis is moved onto blue–yellow and onto lightness.</summary>
    private const float RedGreenKeep = 0.35f;
    private const float RedGreenToBlueYellow = 1.80f;
    private const float RedGreenToLightness = 1.50f;

    /// <summary>Warmth 1 removes this share of blue and of green, in linear light.</summary>
    private const float WarmthBlueCut = 1.0f;
    private const float WarmthGreenCut = 0.30f;

    /// <summary>
    /// A filled area is kept at or below this share of the theme's maximum area contrast, which leaves a margin
    /// before the area guard would consider it.
    /// </summary>
    private const float AreaCeiling = 0.75f;

    /// <summary>
    /// Colours within this log-contrast of the source page are shown exactly as the page. Without it the table
    /// entries on either side of the page colour would both be lifted slightly, and a page that falls between
    /// two entries would come out a shade off.
    /// </summary>
    private const float PageDeadZone = 0.07f;

    private const float Flare = Wcag.Flare;

    private static readonly Grid Nodes = new();

    public static LensTables Build(Theme theme, RoleMap? roles = null, SceneAnalysis? scene = null)
    {
        ArgumentNullException.ThrowIfNull(theme);
        roles ??= RoleMap.Empty;
        scene ??= SceneAnalysis.Empty;

        if (theme.ChangesNothing)
        {
            Lut3D identity = Lut3D.Identity();
            return new LensTables(identity, identity, new FrameParameters { Identity = true }, theme, roles);
        }

        var common = new FrameParameters
        {
            MaxLuminance = theme.Limits.MaxLuminance,
            MaxAreaContrast = theme.Limits.MaxAreaContrast,
            WholeLensLightFraction = scene.LightFraction,
            LocalContrast = theme.Contrast is { IsOn: true, Mode: ContrastMode.Local } ? theme.Contrast.Amount : 0f,
            Sharpen = theme.Sharpen is { IsOn: true, Mode: SharpenMode.Standard } ? theme.Sharpen.Amount : 0f,
            StrongSharpen = theme.Sharpen is { IsOn: true, Mode: SharpenMode.Strong } ? theme.Sharpen.Amount : 0f,
        };

        if (theme.Base == ThemeBase.Keep)
        {
            Lut3D keep = BuildKeepTable(theme, scene, WarmGains(theme.Warmth));
            Vector3 white = keep[Lut3D.Size - 1, Lut3D.Size - 1, Lut3D.Size - 1];
            Vector3 black = keep[0, 0, 0];
            return new LensTables(
                keep,
                keep,
                common with
                {
                    Direction = 0f,
                    PageEncoded = white,
                    TextEncoded = black,
                    PageLuminance = Wcag.Luminance(Srgb.Decode(white)),
                },
                theme,
                roles);
        }

        // Role colours are written down as they look at the theme's own warmth, so only the difference between
        // the current warmth and that one is applied on top of them.
        Vector3 authored = Vector3.Max(WarmGains(theme.AuthoredWarmth), new Vector3(0.05f));
        var context = new RoleContext(theme, WarmGains(theme.Warmth) / authored);
        Lut3D? light = null;
        Lut3D? dark = null;
        Parallel.Invoke(
            () => light = BuildRoleTable(context, roles, RegionPolarity.Light),
            () => dark = BuildRoleTable(context, roles, RegionPolarity.Dark));

        return new LensTables(
            light!,
            dark!,
            common with
            {
                Direction = theme.Base == ThemeBase.Dark ? 1f : -1f,
                PageEncoded = context.PageEncoded,
                TextEncoded = context.TextEncoded,
                PageLuminance = context.PageWarmLuminance,
                GuardStrength = theme.KeepBackground == KeepBackgroundMode.Off ? 0f : 1f,
                GuardSoftGate = theme.KeepBackground == KeepBackgroundMode.Balanced ? 1f : 0f,
            },
            theme,
            roles);
    }

    /// <summary>Runs <paramref name="body"/> for every table entry, one slice of the table per task.</summary>
    private static void ForEachEntry(Action<int> body)
    {
        const int slice = Lut3D.Size * Lut3D.Size;
        Parallel.For(0, Lut3D.Size, b =>
        {
            int end = (b + 1) * slice;
            for (int n = b * slice; n < end; n++)
            {
                body(n);
            }
        });
    }

    private static Vector3 WarmGains(float warmth) =>
        new(1f, 1f - (WarmthGreenCut * warmth), 1f - (WarmthBlueCut * warmth));

    /// <summary>
    /// Red–green assist (section 6.6). It does not restore colour vision: it moves differences that sit on the
    /// red–green axis onto the blue–yellow axis and onto lightness, where they survive protanopia and deuteranopia.
    /// Reds move towards orange-yellow and get lighter; greens move towards blue and get darker.
    /// </summary>
    internal static LumLab RedGreenAssist(LumLab c)
    {
        float j = c.J * (1f + (RedGreenToLightness * c.A));
        return new LumLab(Math.Clamp(j, 0f, 1f), c.A * RedGreenKeep, c.B + (RedGreenToBlueYellow * c.A));
    }

    private static float SoftLimit(float chroma, float? max)
    {
        if (max is not { } m)
        {
            return chroma;
        }

        // Approaches the limit smoothly instead of clipping, so two strong colours stay different.
        return m <= 1e-5f ? 0f : m * (1f - MathF.Exp(-chroma / m));
    }

    /// <summary>Keeps chroma in a range where the conversion back to RGB is well behaved; the gamut fit does the rest.</summary>
    private static LumLab Tame(LumLab c, float? maxChroma)
    {
        float chroma = c.Chroma;
        float limit = MathF.Min(maxChroma ?? float.MaxValue, 0.9f * c.J);
        return chroma > limit && chroma > 1e-6f ? c.WithChromaScaled(limit / chroma) : c;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Keep base: lightness stays as the app drew it.
    // ---------------------------------------------------------------------------------------------------------

    private static Lut3D BuildKeepTable(Theme theme, SceneAnalysis scene, Vector3 warmGains)
    {
        var lut = new Lut3D();
        float maxLum = theme.Limits.MaxLuminance;
        float? maxChroma = theme.Limits.MaxChroma;
        bool redGreen = theme.ColourVision == ColourVisionMode.RedGreen;
        bool contrast = theme.Contrast.IsOn;
        float pivot = Math.Clamp(scene.MedianLightness, 0.05f, 0.95f);
        float gamma = 1f + theme.Contrast.Amount;

        ForEachEntry(n =>
        {
            Vector3 linear = Nodes.Linear[n];
            float y = Nodes.Luminance[n];

            if (contrast && y > 1e-6f)
            {
                // Steepen around the median lightness of the picture; black, the median and white stay put.
                float j = MathF.Cbrt(y);
                float curved = j < pivot
                    ? pivot * MathF.Pow(j / pivot, gamma)
                    : 1f - ((1f - pivot) * MathF.Pow((1f - j) / (1f - pivot), gamma));
                linear = Gamut.Fit(linear * (curved * curved * curved / y));
            }

            if (redGreen || maxChroma is not null)
            {
                LumLab c = LumLab.FromLinear(linear);
                if (redGreen)
                {
                    c = RedGreenAssist(c);
                }

                if (maxChroma is not null)
                {
                    float chroma = c.Chroma;
                    c = chroma > 1e-6f ? c.WithChromaScaled(SoftLimit(chroma, maxChroma) / chroma) : c;
                }

                linear = Gamut.Fit(Tame(c, null).ToLinear());
            }

            // A brightness limit on the keep base dims the whole picture evenly, like turning the backlight down,
            // so nothing is flattened at the top.
            linear *= maxLum;
            linear *= warmGains;

            Vector3 encoded = Srgb.EncodeFast(linear);
            int i = n * 3;
            lut.Data[i] = encoded.X;
            lut.Data[i + 1] = encoded.Y;
            lut.Data[i + 2] = encoded.Z;
        });

        return lut;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Dark and light bases: lightness is remapped onto the theme's roles.
    // ---------------------------------------------------------------------------------------------------------

    private static Lut3D BuildRoleTable(RoleContext context, RoleMap roles, RegionPolarity region)
    {
        var mapper = new RegionMapper(context, roles, region);
        var lut = new Lut3D();
        ForEachEntry(n =>
        {
            Vector3 encoded = mapper.Map(Nodes.Lab[n], Nodes.Luminance[n]);
            int i = n * 3;
            lut.Data[i] = encoded.X;
            lut.Data[i + 1] = encoded.Y;
            lut.Data[i + 2] = encoded.Z;
        });
        return lut;
    }

    /// <summary>The parts of a role table that depend only on the theme.</summary>
    private sealed class RoleContext
    {
        public RoleContext(Theme theme, Vector3 warmGains)
        {
            Theme = theme;
            Roles = theme.RequireRoles();
            WarmGains = warmGains;
            TextIsLighter = theme.Base == ThemeBase.Dark;
            MaxLuminance = theme.Limits.MaxLuminance;
            PageLuminance = MathF.Min(Wcag.Luminance(Roles.Page), MaxLuminance);

            // The most contrast the theme can show: up to the brightness limit on a dark base, down to black on a light one.
            LogMax = OutputLog(TextIsLighter ? MaxLuminance : 0f);
            LogText = Math.Clamp(OutputLog(Wcag.Luminance(Roles.Text)), 0.05f, LogMax);
            LogQuiet = Math.Clamp(OutputLog(Wcag.Luminance(Roles.QuietText)), 0f, LogText);
            LogPanel = Math.Clamp(OutputLog(Wcag.Luminance(Roles.Panel)), 0f, LogQuiet);

            // The theme's neutral tint as a function of how far from the page a colour ends up.
            var tint = new List<(float Y, LumLab Lab)> { (0f, LumLab.FromRgb8(Roles.Page)) };
            AddTint(tint, LogPanel, Roles.Panel);
            AddTint(tint, LogQuiet, Roles.QuietText);
            AddTint(tint, LogText, Roles.Text);
            TintStops = tint.ToArray();

            Vector3 pageLinear = Gamut.Fit(Limit(LumLab.FromRgb8(Roles.Page)).ToLinear()) * warmGains;
            PageWarmLuminance = Wcag.Luminance(pageLinear);
            PageEncoded = Srgb.Encode(Vector3.Clamp(pageLinear, Vector3.Zero, Vector3.One));
            TextEncoded = Finish(Limit(LumLab.FromRgb8(Roles.Text)));
        }

        public Theme Theme { get; }

        public ThemeRoles Roles { get; }

        public Vector3 WarmGains { get; }

        public bool TextIsLighter { get; }

        public float MaxLuminance { get; }

        public float PageLuminance { get; }

        public float PageWarmLuminance { get; }

        public float LogMax { get; }

        public float LogText { get; }

        public float LogQuiet { get; }

        public float LogPanel { get; }

        public (float Y, LumLab Lab)[] TintStops { get; }

        public Vector3 PageEncoded { get; }

        public Vector3 TextEncoded { get; }

        private static void AddTint(List<(float Y, LumLab Lab)> stops, float y, Rgb8 colour)
        {
            if (y > stops[^1].Y + 1e-3f)
            {
                stops.Add((y, LumLab.FromRgb8(colour)));
            }
        }

        /// <summary>Log of the contrast a luminance has against the theme's page, positive on the text side.</summary>
        public float OutputLog(float luminance) =>
            TextIsLighter
                ? MathF.Log((luminance + Flare) / (PageLuminance + Flare))
                : MathF.Log((PageLuminance + Flare) / (luminance + Flare));

        public float LuminanceAt(float log)
        {
            float y = TextIsLighter
                ? ((PageLuminance + Flare) * MathF.Exp(log)) - Flare
                : ((PageLuminance + Flare) * MathF.Exp(-log)) - Flare;
            return Math.Clamp(y, 0f, MaxLuminance);
        }

        public (float A, float B) TintAt(float log)
        {
            (float Y, LumLab Lab)[] s = TintStops;
            if (log <= s[0].Y)
            {
                return (s[0].Lab.A, s[0].Lab.B);
            }

            for (int i = 1; i < s.Length; i++)
            {
                if (log <= s[i].Y)
                {
                    float t = (log - s[i - 1].Y) / (s[i].Y - s[i - 1].Y);
                    return (
                        s[i - 1].Lab.A + ((s[i].Lab.A - s[i - 1].Lab.A) * t),
                        s[i - 1].Lab.B + ((s[i].Lab.B - s[i - 1].Lab.B) * t));
                }
            }

            return (s[^1].Lab.A, s[^1].Lab.B);
        }

        /// <summary>Applies the brightness and chroma limits to a colour in the working space.</summary>
        public LumLab Limit(LumLab c)
        {
            float j = Math.Clamp(c.J, 0f, MathF.Cbrt(MaxLuminance));
            return Tame(new LumLab(j, c.A, c.B), Theme.Limits.MaxChroma is { } m ? m * 1.02f : null);
        }

        /// <summary>
        /// The last steps for one table entry: into gamut, warmth, and the correction that keeps text-like
        /// colours at the contrast they had before warming (section 6.6).
        /// </summary>
        public Vector3 Finish(LumLab limited)
        {
            Vector3 linear = Gamut.Fit(limited.ToLinear());
            if (WarmGains != Vector3.One)
            {
                float before = Wcag.Luminance(linear);
                Vector3 warm = linear * WarmGains;
                float after = Wcag.Luminance(warm);

                float contrastBefore = SignedContrast(before, PageLuminance);
                float contrastAfter = SignedContrast(after, PageWarmLuminance);
                float wanted = MathF.Min(contrastBefore, Theme.Limits.MinTextContrast);
                if (contrastAfter < wanted)
                {
                    float needed = Wcag.LuminanceAtContrast(PageWarmLuminance, wanted, TextIsLighter);
                    warm = Gamut.WithLuminance(warm, Math.Clamp(needed, 0f, MaxLuminance));
                }

                linear = warm;
            }

            return Srgb.EncodeFast(linear);
        }

        private float SignedContrast(float luminance, float page) =>
            TextIsLighter ? (luminance + Flare) / (page + Flare) : (page + Flare) / (luminance + Flare);
    }

    /// <summary>Maps source colours for one region polarity (light or dark parts of the screen).</summary>
    private sealed class RegionMapper
    {
        private readonly RoleContext _c;
        private readonly bool _sourceTextIsDarker;
        private readonly float _sourcePage;
        private readonly PiecewiseLinear _curve;
        private readonly float _otherSideCap;
        private readonly float _contrastGamma;
        private readonly bool _redGreen;
        private readonly Accent[] _accents;

        public RegionMapper(RoleContext context, RoleMap roles, RegionPolarity region)
        {
            _c = context;
            _sourceTextIsDarker = region == RegionPolarity.Light;
            _redGreen = context.Theme.ColourVision == ColourVisionMode.RedGreen;
            _contrastGamma = context.Theme.Contrast.IsOn ? 1f + (1.5f * context.Theme.Contrast.Amount) : 1f;

            // With no page found for this region, everything from the assumed page colour outwards counts as page.
            _otherSideCap = roles.Anchor(region, Role.Page) is null ? 0f : context.LogPanel;
            _sourcePage = roles.Anchor(region, Role.Page)?.Cluster.Luminance
                ?? (_sourceTextIsDarker ? DefaultLightPage : DefaultDarkPage);
            float logFarthest = SourceDistance(_sourceTextIsDarker ? 0f : 1f);

            // Text anchor. A source "text" with very little contrast is not allowed to stretch the curve without limit.
            float logSourceText = roles.Anchor(region, Role.Text) is { } text ? SourceDistance(text.Cluster.Luminance) : -1f;
            if (logSourceText <= 0.05f)
            {
                logSourceText = SourceDistance(_sourceTextIsDarker ? DefaultLightText : DefaultDarkText);
            }

            logSourceText = Math.Clamp(MathF.Max(logSourceText, context.LogText / MaxSlope), 0.05f, MathF.Max(logFarthest, 0.05f));

            // Priority order: page, text, then the optional anchors. A conflicting optional anchor is dropped.
            var points = new List<(float X, float Y)>
            {
                (0f, 0f),
                (logSourceText, context.LogText),
            };
            if (logFarthest > logSourceText + 1e-3f)
            {
                // Beyond the text anchor the curve carries on a little, up to what the theme's limits allow.
                points.Add((logFarthest, context.LogText + ((context.LogMax - context.LogText) * 0.6f)));
            }

            if (roles.Anchor(region, Role.Panel) is { } panel && context.LogPanel > 0f)
            {
                float x = SourceDistance(panel.Cluster.Luminance);
                if (x > 0f && x < logSourceText * 0.6f)
                {
                    points.Add((x, context.LogPanel));
                }
            }

            if (roles.Anchor(region, Role.QuietText) is { } quiet && context.LogQuiet < context.LogText)
            {
                float x = SourceDistance(quiet.Cluster.Luminance);
                if (x > 0f && x < logSourceText * 0.95f)
                {
                    points.Add((x, context.LogQuiet));
                }
            }

            _curve = PiecewiseLinear.NonDecreasing(points);

            // Accent anchors: where the cluster's colour lands by the general rule, and where the theme wants it.
            var accents = new List<Accent>();
            foreach (RoleEntry entry in roles.Anchors(region))
            {
                if (entry.Role.IsNeutral())
                {
                    continue;
                }

                LumLab source = LumLab.FromRgb8(entry.Cluster.Colour);
                LumLab general = General(source, entry.Cluster.Luminance);
                LumLab target = general;
                if (entry.Role.IsThemedAccent())
                {
                    LumLab themed = LumLab.FromRgb8(context.Roles[entry.Role]);
                    if (entry.IsFill && themed.J > 1e-3f)
                    {
                        // A fill takes the theme's hue but stays about as light or dark as the general rule made
                        // it: it is a background, and whatever is written on it was mapped by that same rule.
                        // It is also held under the area limit, so a button keeps its colour instead of being
                        // mistaken for a background that came out wrong.
                        float ceiling = MathF.Cbrt(context.LuminanceAt(MathF.Log(AreaCeiling * context.Theme.Limits.MaxAreaContrast)));
                        float j = context.TextIsLighter ? MathF.Min(general.J, ceiling) : MathF.Max(general.J, ceiling);
                        float scale = MathF.Min(j / themed.J, 1.2f);
                        target = new LumLab(j, themed.A * scale, themed.B * scale);
                    }
                    else
                    {
                        target = themed;
                    }
                }

                accents.Add(new Accent(source, target.J - general.J, target.A - general.A, target.B - general.B));
            }

            _accents = accents.ToArray();
        }

        /// <summary>Log of the contrast a source luminance has against the source page, positive on the text side.</summary>
        private float SourceLog(float luminance) =>
            _sourceTextIsDarker
                ? MathF.Log((_sourcePage + Flare) / (luminance + Flare))
                : MathF.Log((luminance + Flare) / (_sourcePage + Flare));

        /// <summary><see cref="SourceLog"/> with the dead zone around the page taken out, so that it still starts at zero.</summary>
        private float SourceDistance(float luminance)
        {
            float x = SourceLog(luminance);
            return MathF.Sign(x) * MathF.Max(0f, MathF.Abs(x) - PageDeadZone);
        }

        /// <summary>The rule for a colour that belongs to no accent cluster.</summary>
        private LumLab General(LumLab source, float sourceLuminance)
        {
            float x = SourceDistance(sourceLuminance);
            float chroma = source.Chroma;
            float coloured = Smooth.Step(GreyChroma, ColouredChroma, chroma);

            // Only real colour counts as contrast. Near-greys must not, or the table entries just beside the grey
            // axis would be lifted and put a bump into every grey ramp that passes between them.
            float colourAsContrast = ChromaAsContrast * chroma * coloured;

            float log = x >= 0f
                ? _curve.Evaluate(MathF.Sqrt((x * x) + (colourAsContrast * colourAsContrast)))
                : MathF.Max(MathF.Min(-x, _otherSideCap), _curve.Evaluate(colourAsContrast));

            if (_contrastGamma > 1f && log < _c.LogText)
            {
                // Contrast, Standard: steepen around the midpoint between Page and Text. Both ends stay where they are.
                float u = log / _c.LogText;
                float up = MathF.Pow(u, _contrastGamma);
                float down = MathF.Pow(1f - u, _contrastGamma);
                log = _c.LogText * (up / (up + down));
            }

            float j = MathF.Cbrt(_c.LuminanceAt(log));
            (float tintA, float tintB) = _c.TintAt(log);
            float keep = chroma > 1e-6f ? SoftLimit(chroma, _c.Theme.Limits.MaxChroma) / chroma : 0f;
            var own = new LumLab(j, source.A * keep, source.B * keep);
            if (_redGreen)
            {
                // Applied to the source's own colour only. The theme's neutral tint is not a red or a green.
                own = RedGreenAssist(own);
            }

            return new LumLab(
                Smooth.Lerp(j, own.J, coloured),
                Smooth.Lerp(tintA, own.A, coloured),
                Smooth.Lerp(tintB, own.B, coloured));
        }

        public Vector3 Map(LumLab source, float sourceLuminance)
        {
            LumLab result = General(source, sourceLuminance);

            if (_accents.Length > 0)
            {
                float coloured = Smooth.Step(GreyChroma, ColouredChroma, source.Chroma);
                if (coloured > 0f)
                {
                    float total = 0f;
                    float dj = 0f;
                    float da = 0f;
                    float db = 0f;
                    foreach (Accent accent in _accents)
                    {
                        // Lightness counts half: a lighter or darker shade of an accent is still that accent.
                        float l = (source.J - accent.Source.J) * 0.5f;
                        float a = source.A - accent.Source.A;
                        float b = source.B - accent.Source.B;
                        // The weight grows without bound towards the anchor, so each anchor's own colour lands
                        // exactly on its target however close the next anchor is.
                        float d2 = ((l * l) + (a * a) + (b * b)) / (AccentReach * AccentReach);
                        if (d2 > 12f)
                        {
                            continue;   // further than three and a half reaches: the pull is below one in a million
                        }

                        float w = MathF.Exp(-d2) / (d2 + 0.02f);
                        total += w;
                        dj += w * accent.DeltaJ;
                        da += w * accent.DeltaA;
                        db += w * accent.DeltaB;
                    }

                    float scale = coloured / MathF.Max(1f, total);
                    result = new LumLab(result.J + (dj * scale), result.A + (da * scale), result.B + (db * scale));
                }
            }

            return _c.Finish(_c.Limit(result));
        }

        private readonly record struct Accent(LumLab Source, float DeltaJ, float DeltaA, float DeltaB);
    }

    /// <summary>The 35,937 input colours of a table, converted once.</summary>
    private sealed class Grid
    {
        public Grid()
        {
            Linear = new Vector3[Lut3D.EntryCount];
            Lab = new LumLab[Lut3D.EntryCount];
            Luminance = new float[Lut3D.EntryCount];
            int n = 0;
            for (int b = 0; b < Lut3D.Size; b++)
            {
                for (int g = 0; g < Lut3D.Size; g++)
                {
                    for (int r = 0; r < Lut3D.Size; r++)
                    {
                        Vector3 linear = Srgb.Decode(new Vector3(r, g, b) / (Lut3D.Size - 1));
                        Linear[n] = linear;
                        Lab[n] = LumLab.FromLinear(linear);
                        Luminance[n] = Wcag.Luminance(linear);
                        n++;
                    }
                }
            }
        }

        public Vector3[] Linear { get; }

        public LumLab[] Lab { get; }

        public float[] Luminance { get; }
    }
}
