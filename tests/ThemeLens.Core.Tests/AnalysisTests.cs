// SPDX-License-Identifier: GPL-3.0-or-later
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Imaging;
using ThemeLens.Core.Rendering;
using ThemeLens.Core.Themes;
using ThemeLens.Testing;

namespace ThemeLens.Core.Tests;

public class AnalysisTests
{
    public static TheoryData<string> FixtureNames => new(Fixtures.Names);

    private static SceneAnalysis Analyse(ImageRgb8 image) => ClusterFinder.Analyse(Neighbourhood.Build(image).ToThumbnail());

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void At_most_eight_clusters_whose_shares_add_up_to_the_whole_lens(string fixture)
    {
        SceneAnalysis scene = Analyse(Fixtures.Draw(fixture).Image);
        scene.Clusters.Count.ShouldBeInRange(1, ClusterFinder.MaxClusters);
        scene.Clusters.Sum(c => c.Share).ShouldBe(1f, 1e-3f);
        scene.Clusters.Select(c => c.Share).ShouldBeInOrder(SortDirection.Descending);
    }

    [Theory]
    [MemberData(nameof(FixtureNames))]
    public void The_same_picture_always_gives_the_same_clusters(string fixture)
    {
        ImageRgb8 image = Fixtures.Draw(fixture).Image;
        Analyse(image).Clusters.SequenceEqual(Analyse(image).Clusters).ShouldBeTrue();
    }

    [Fact]
    public void A_flat_picture_is_one_cluster_of_exactly_that_colour()
    {
        var colour = new Rgb8(18, 52, 86);
        SceneAnalysis scene = Analyse(ImageRgb8.Filled(320, 200, colour));
        scene.Clusters.Count.ShouldBe(1);
        scene.Clusters[0].Colour.ShouldBe(colour);
        scene.Clusters[0].Share.ShouldBe(1f);
        scene.Clusters[0].Thinness.ShouldBe(0f);
        scene.Clusters[0].EdgeTouch.ShouldBe(1f);
    }

    [Fact]
    public void A_white_page_and_a_light_grey_panel_are_kept_apart()
    {
        // They are close together, and a table that merged them could not show the panel as a panel.
        var c = new Canvas(320, 200, Rgb8.White);
        c.Fill(40, 40, 240, 120, Rgb8.FromHex("#F1F3F5"));
        SceneAnalysis scene = Analyse(c.Image);
        scene.Clusters.Select(x => x.Colour).ShouldBe([Rgb8.White, Rgb8.FromHex("#F1F3F5")], ignoreOrder: true);
    }

    [Fact]
    public void A_small_strongly_coloured_marker_is_kept_but_a_faint_speck_is_not()
    {
        var c = new Canvas(640, 400, Rgb8.White);
        c.Fill(100, 100, 14, 14, Rgb8.FromHex("#D93025"));   // 0.08% of the lens, strongly coloured
        c.Fill(300, 100, 14, 14, Rgb8.FromHex("#E8E8E8"));   // same size, almost the page colour
        SceneAnalysis scene = Analyse(c.Image);
        scene.Clusters.ShouldContain(x => x.Colour == Rgb8.FromHex("#D93025"));
        scene.Clusters.ShouldNotContain(x => x.Colour == Rgb8.FromHex("#E8E8E8"));
    }

    [Fact]
    public void Text_is_thin_and_reports_its_true_colour_not_an_edge_blend()
    {
        Fixture document = Fixtures.Draw("light-document.png");
        SceneAnalysis scene = Analyse(document.Image);
        ColourCluster text = scene.Clusters.Single(c => c.Colour == document.Text);
        text.Thinness.ShouldBeGreaterThan(0.9f);
        ColourCluster page = scene.Clusters.Single(c => c.Colour == document.Page);
        page.Thinness.ShouldBeLessThan(0.4f);
        page.EdgeTouch.ShouldBeGreaterThan(0.5f);
    }

    [Fact]
    public void Each_half_of_a_mixed_screen_is_put_in_its_own_region()
    {
        SceneAnalysis scene = Analyse(Fixtures.Draw("mixed-halves.png").Image);
        scene.Clusters.Single(c => c.Colour == Rgb8.White).Region.ShouldBe(RegionPolarity.Light);
        scene.Clusters.Single(c => c.Colour == Rgb8.FromHex("#1E1E1E")).Region.ShouldBe(RegionPolarity.Dark);
        scene.LightFraction.ShouldBeInRange(0.3f, 0.7f);
    }

    [Fact]
    public void A_mid_tone_fill_belongs_to_the_region_around_it()
    {
        // A blue button is neither a light page nor a dark one.
        SceneAnalysis scene = Analyse(Fixtures.Draw("web-buttons.png").Image);
        scene.Clusters.Single(c => c.Colour == Rgb8.FromHex("#1A73E8")).Region.ShouldBe(RegionPolarity.Light);
        scene.LightFraction.ShouldBeGreaterThan(0.9f);
    }

    [Fact]
    public void A_light_document_gets_the_roles_of_the_specification_wireframe()
    {
        RoleMap roles = RoleAssigner.Assign(Analyse(Fixtures.Draw("light-document.png").Image));
        RoleOf(roles, "#FFFFFF").ShouldBe(Role.Page);
        RoleOf(roles, "#222222").ShouldBe(Role.Text);
        RoleOf(roles, "#F1F3F5").ShouldBe(Role.Panel);
        RoleOf(roles, "#1A73E8").ShouldBe(Role.Link);
        RoleOf(roles, "#2E9E44").ShouldBe(Role.Good);
        RoleOf(roles, "#D93025").ShouldBe(Role.Bad);
        roles.Anchor(RegionPolarity.Light, Role.Page)!.Cluster.Colour.ShouldBe(Rgb8.White);
        roles.Anchor(RegionPolarity.Dark, Role.Page).ShouldBeNull();
    }

    [Theory]
    [InlineData(29f, Role.Bad)]
    [InlineData(355f, Role.Bad)]
    [InlineData(70f, Role.Warning)]
    [InlineData(110f, Role.Warning)]
    [InlineData(146f, Role.Good)]
    [InlineData(258f, Role.Link)]
    [InlineData(320f, Role.OtherAccent)]
    [InlineData(190f, Role.OtherAccent)]
    public void Hue_families_follow_the_specification(float hue, Role expected) => RoleAssigner.HueFamily(hue).ShouldBe(expected);

    [Fact]
    public void A_pin_overrides_the_automatic_role_and_marks_the_row()
    {
        SceneAnalysis scene = Analyse(Fixtures.Draw("light-document.png").Image);
        var pin = new Pin(Rgb8.FromHex("#D93025"), RegionPolarity.Light, Role.Warning);
        RoleMap roles = RoleAssigner.Assign(scene, [pin]);
        RoleEntry entry = roles.Entries.Single(e => e.Cluster.Colour == Rgb8.FromHex("#D93025"));
        entry.Role.ShouldBe(Role.Warning);
        entry.Pinned.ShouldBeTrue();
        entry.IsAnchor.ShouldBeTrue();
        roles.Entries.Count(e => e.Pinned).ShouldBe(1);
    }

    [Fact]
    public void A_pin_holds_even_when_its_colour_is_not_on_screen()
    {
        SceneAnalysis scene = Analyse(Fixtures.Draw("dark-editor.png").Image);
        var pin = new Pin(Rgb8.FromHex("#FF00FF"), RegionPolarity.Dark, Role.Good);
        RoleMap roles = RoleAssigner.Assign(scene, [pin]);
        RoleEntry entry = roles.Entries.Single(e => e.Pinned);
        entry.Cluster.Colour.ShouldBe(Rgb8.FromHex("#FF00FF"));
        entry.Cluster.Share.ShouldBe(0f);
        entry.Role.ShouldBe(Role.Good);
        entry.Region.ShouldBe(RegionPolarity.Dark);
    }

    [Fact]
    public void The_rim_of_a_coloured_shape_is_not_an_accent_of_its_own()
    {
        // The anti-aliased edge of a pill is a blend of the pill and the panel; as its own accent it would glow.
        RoleMap roles = RoleAssigner.Assign(Analyse(Fixtures.Draw("status-dashboard.png").Image));
        IEnumerable<RoleEntry> greens = roles.Entries.Where(e => e.Cluster.Chroma > 0.07f && RoleAssigner.HueFamily(e.Cluster.HueDegrees) == Role.Good);
        greens.Count(e => e.Role == Role.Good).ShouldBe(1);
        greens.Where(e => e.Role != Role.Good).ShouldAllBe(e => !e.IsAnchor);
    }

    private static Role RoleOf(RoleMap roles, string hex) =>
        roles.Entries.Single(e => e.Cluster.Colour == Rgb8.FromHex(hex)).Role;
}
