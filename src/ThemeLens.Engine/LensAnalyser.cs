// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Themes;

namespace ThemeLens.Engine;

/// <summary>What one analysis produced: the same clusters and mapping go to the lens and to the control window (figure 7).</summary>
public sealed record AnalysisResult(SceneAnalysis Scene, RoleMap Roles, LensTables Tables, bool LookChanged)
{
    /// <summary>
    /// The rectangles found for the "rectangles only" option (section 5.6), or null when the theme that was
    /// analysed for did not have the option on.
    /// </summary>
    public FoundRectangles? Rectangles { get; init; }

    /// <summary>The base the rectangles were found for: bright ones for a dark base, dark ones for a light base.</summary>
    public ThemeBase RectanglesBase { get; init; }
}

/// <summary>
/// The slow loop of figure 7: thumbnail in, lookup tables out. It also keeps the picture steady (section 6.4):
/// a new set of role anchors has to win three analyses in a row before it replaces the current one, unless the
/// whole scene has changed.
/// </summary>
/// <remarks>Calls must not overlap. The analysis runners guarantee that.</remarks>
public sealed class LensAnalyser
{
    /// <summary>A cluster must win a role in this many analyses in a row before the role changes.</summary>
    public const int AnalysesToChangeRoles = 3;

    /// <summary>A jump this large in mean luminance means the scene changed, and the new roles apply at once.</summary>
    public const float SceneChangeLuminance = 0.15f;

    /// <summary>Two anchors are the same colour when they are this close in OKLab.</summary>
    private const float SameColour = 0.04f;

    private readonly Lock _gate = new();
    private Theme _theme;
    private IReadOnlyList<Pin> _pins = [];
    private RoleMap? _accepted;
    private RoleMap? _candidate;
    private int _candidateCount;
    private SceneAnalysis _scene = SceneAnalysis.Empty;
    private SceneAnalysis _builtScene = SceneAnalysis.Empty;
    private LensTables _tables;
    private bool _forceRebuild;
    private long _screenPixels;

    public LensAnalyser(Theme theme)
    {
        _theme = theme;
        _tables = TableBuilder.Build(theme);
    }

    public Theme Theme
    {
        get
        {
            lock (_gate)
            {
                return _theme;
            }
        }
    }

    /// <summary>The look built most recently.</summary>
    public LensTables Tables
    {
        get
        {
            lock (_gate)
            {
                return _tables;
            }
        }
    }

    public RoleMap Roles
    {
        get
        {
            lock (_gate)
            {
                return _accepted ?? RoleMap.Empty;
            }
        }
    }

    /// <summary>Changes the theme and rebuilds the look from the current mapping straight away.</summary>
    public LensTables SetTheme(Theme theme)
    {
        lock (_gate)
        {
            _theme = theme;
            _tables = TableBuilder.Build(theme, _accepted, _scene);
            _builtScene = _scene;
            return _tables;
        }
    }

    /// <summary>
    /// Size of the screen the lens is on, in pixels. The "rectangles only" option measures areas as shares of
    /// it. Zero (the start value) means the lens area itself is taken for the screen.
    /// </summary>
    public long ScreenPixels
    {
        get => Interlocked.Read(ref _screenPixels);
        set => Interlocked.Exchange(ref _screenPixels, Math.Max(0, value));
    }

    /// <summary>Replaces the user's pinned mappings. They take effect with the next analysis, without the three-in-a-row wait.</summary>
    public void SetPins(IReadOnlyList<Pin> pins)
    {
        lock (_gate)
        {
            _pins = pins;
            _forceRebuild = true;
        }
    }

    /// <summary>Forgets what was on screen, so the next analysis is accepted at once. Used when the lens is turned on.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _accepted = null;
            _candidate = null;
            _candidateCount = 0;
            _scene = SceneAnalysis.Empty;
            _tables = TableBuilder.Build(_theme);
            _builtScene = _scene;
        }
    }

    public AnalysisResult Process(Thumbnail thumbnail)
    {
        Theme theme;
        IReadOnlyList<Pin> pins;
        bool force;
        SceneAnalysis previous;
        lock (_gate)
        {
            theme = _theme;
            pins = _pins;
            force = _forceRebuild;
            _forceRebuild = false;
            previous = _scene;
        }

        // The rectangles belong to the theme this analysis started with; the caller checks that it is still the one.
        Theme analysed = theme;
        FoundRectangles? rectangles = null;
        if (analysed.RectanglesOnly)
        {
            long screen = ScreenPixels;
            if (screen <= 0)
            {
                screen = (long)thumbnail.Width * thumbnail.Height * thumbnail.SourcePixelsPerSample * thumbnail.SourcePixelsPerSample;
            }

            rectangles = RectangleFinder.Find(thumbnail, analysed.Rectangles, analysed.Base, screen);
        }

        SceneAnalysis scene = ClusterFinder.Analyse(thumbnail);
        RoleMap proposed = RoleAssigner.Assign(scene, pins);
        bool sceneChanged = MathF.Abs(scene.MeanLuminance - previous.MeanLuminance) > SceneChangeLuminance;

        lock (_gate)
        {
            // The theme may have been changed while this analysis ran; build for the one that is current now.
            theme = _theme;
            _scene = scene;
            bool rolesChanged = false;
            if (_accepted is null || force || sceneChanged)
            {
                rolesChanged = _accepted is null || !SameAnchors(_accepted, proposed);
                _accepted = proposed;
                _candidate = null;
                _candidateCount = 0;
            }
            else if (SameAnchors(_accepted, proposed))
            {
                // Same mapping as before: take the fresh shares and colours for the control window.
                _accepted = proposed;
                _candidate = null;
                _candidateCount = 0;
            }
            else
            {
                _candidateCount = _candidate is not null && SameAnchors(_candidate, proposed) ? _candidateCount + 1 : 1;
                _candidate = proposed;
                if (_candidateCount >= AnalysesToChangeRoles)
                {
                    _accepted = proposed;
                    _candidate = null;
                    _candidateCount = 0;
                    rolesChanged = true;
                }
            }

            // The tables also depend on two whole-scene numbers; rebuild when either has drifted noticeably.
            bool statsChanged =
                MathF.Abs(scene.LightFraction - _builtScene.LightFraction) > 0.2f
                || (theme.Base == ThemeBase.Keep && theme.Contrast.IsOn
                    && MathF.Abs(scene.MedianLightness - _builtScene.MedianLightness) > 0.08f);

            bool rebuild = rolesChanged || force || statsChanged || !ReferenceEquals(_tables.Theme, theme);
            if (rebuild)
            {
                _tables = TableBuilder.Build(theme, _accepted, scene);
                _builtScene = scene;
            }

            return new AnalysisResult(scene, _accepted, _tables, rebuild)
            {
                Rectangles = rectangles,
                RectanglesBase = analysed.Base,
            };
        }
    }

    /// <summary>True when both maps fix the same colours to the same roles.</summary>
    public static bool SameAnchors(RoleMap a, RoleMap b)
    {
        return Covers(a, b) && Covers(b, a);

        static bool Covers(RoleMap x, RoleMap y)
        {
            foreach (RoleEntry e in x.Entries)
            {
                if (!e.IsAnchor)
                {
                    continue;
                }

                bool found = false;
                foreach (RoleEntry o in y.Entries)
                {
                    if (o.IsAnchor && o.Role == e.Role && o.Region == e.Region && o.IsFill == e.IsFill
                        && Oklab.Distance(o.Cluster.Lab, e.Cluster.Lab) <= SameColour)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

/// <summary>Runs analyses off the frame loop, one at a time.</summary>
public interface IAnalysisRunner
{
    bool IsBusy { get; }

    void Submit(Func<AnalysisResult> work);

    bool TryTake(out AnalysisResult? result);
}

/// <summary>Runs each analysis on the spot. Used by tests, where everything happens on one thread and a fake clock.</summary>
public sealed class InlineAnalysisRunner : IAnalysisRunner
{
    private AnalysisResult? _result;

    public bool IsBusy => false;

    public void Submit(Func<AnalysisResult> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        _result = work();
    }

    public bool TryTake(out AnalysisResult? result)
    {
        result = _result;
        _result = null;
        return result is not null;
    }
}

/// <summary>Runs analyses on the thread pool. A slow analysis only means a slightly out-of-date table.</summary>
public sealed class BackgroundAnalysisRunner : IAnalysisRunner
{
    private AnalysisResult? _result;
    private int _busy;

    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public void Submit(Func<AnalysisResult> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Volatile.Write(ref _result, work());
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        });
    }

    public bool TryTake(out AnalysisResult? result)
    {
        result = Interlocked.Exchange(ref _result, null);
        return result is not null;
    }
}
