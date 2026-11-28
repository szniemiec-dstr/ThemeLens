// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Mapping;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;

namespace ThemeLens.Testing;

/// <summary>One still frame taken through the whole lens: analyse, assign roles, build tables, render.</summary>
public sealed record LensResult(ImageRgb8 Source, SceneAnalysis Scene, RoleMap Roles, LensTables Tables, ImageRgb8 Output);

public static class Pipeline
{
    public static (SceneAnalysis Scene, RoleMap Roles) Analyse(ImageRgb8 source, IReadOnlyList<Pin>? pins = null)
    {
        SceneAnalysis scene = ClusterFinder.Analyse(Neighbourhood.Build(source).ToThumbnail());
        return (scene, RoleAssigner.Assign(scene, pins));
    }

    public static LensResult Run(Theme theme, ImageRgb8 source, IReadOnlyList<Pin>? pins = null)
    {
        Thumbnail thumbnail = Neighbourhood.Build(source).ToThumbnail(withDetail: theme.RectanglesOnly);
        SceneAnalysis scene = ClusterFinder.Analyse(thumbnail);
        RoleMap roles = RoleAssigner.Assign(scene, pins);
        LensTables tables = TableBuilder.Build(theme, roles, scene);
        if (theme.RectanglesOnly)
        {
            tables = tables.WithRectangles(RectangleFinder.Find(thumbnail.Detail ?? thumbnail, theme.Rectangles.Values, theme.Rectangles.Target));
        }

        return new LensResult(source, scene, roles, tables, ReferenceRenderer.Render(source, tables));
    }

    /// <summary>
    /// Renders <paramref name="source"/> with tables that were built for a different picture: what the lens
    /// shows in the first frames after the screen changes, before a new analysis has arrived.
    /// </summary>
    public static LensResult RunWithStaleTables(Theme theme, ImageRgb8 analysed, ImageRgb8 source)
    {
        (SceneAnalysis scene, RoleMap roles) = Analyse(analysed);
        LensTables tables = TableBuilder.Build(theme, roles, scene);
        return new LensResult(source, scene, roles, tables, ReferenceRenderer.Render(source, tables));
    }
}
