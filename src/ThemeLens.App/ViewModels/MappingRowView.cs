// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using ThemeLens.Core.Analysis;
using ThemeLens.Core.Colour;
using ThemeLens.Core.Themes;
using ThemeLens.Engine;

namespace ThemeLens.App.ViewModels;

/// <summary>A role as the "Used as" list shows it.</summary>
public sealed record RoleChoice(Role Role, string Name)
{
    public override string ToString() => Name;
}

/// <summary>
/// One row of "Colours on screen now, and what you see" (figure 5): a colour found under the lens, how much of
/// the lens it covers, the role it was given, and the colour it is shown as. Every swatch comes with its name
/// and hex value, so nothing is told by colour alone. Choosing another role pins it.
/// </summary>
public sealed partial class MappingRowView : ObservableObject
{
    private readonly Action<MappingRow, Role> _pin;
    private bool _showing;

    public MappingRowView(MappingRow row, IReadOnlyList<RoleChoice> roles, Action<MappingRow, Role> pin)
    {
        ArgumentNullException.ThrowIfNull(row);
        Roles = roles;
        _pin = pin;
        Row = row;
        Show(row);
    }

    /// <summary>The row of the engine this view shows at the moment.</summary>
    public MappingRow Row { get; private set; }

    public IReadOnlyList<RoleChoice> Roles { get; }

    [ObservableProperty]
    public partial Brush FoundBrush { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial string FoundText { get; set; } = string.Empty;

    /// <summary>"on light" or "on dark": which kind of region the colour was found in.</summary>
    [ObservableProperty]
    public partial string RegionText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShareText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial RoleChoice? SelectedRole { get; set; }

    [ObservableProperty]
    public partial Brush ShownBrush { get; set; } = Brushes.Transparent;

    [ObservableProperty]
    public partial string ShownText { get; set; } = string.Empty;

    /// <summary>The contrast against the page for colours that are read against it, and "Pinned" for a role the user chose.</summary>
    [ObservableProperty]
    public partial string ShownDetail { get; set; } = string.Empty;

    /// <summary>The whole row as one sentence, for screen readers.</summary>
    [ObservableProperty]
    public partial string Spoken { get; set; } = string.Empty;

    /// <summary>The name of this row's role list, for screen readers.</summary>
    [ObservableProperty]
    public partial string RoleListName { get; set; } = string.Empty;

    /// <summary>True when this view can go on showing <paramref name="row"/>: the same colour in the same kind of region.</summary>
    public bool IsFor(MappingRow row) => row.Found == Row.Found && row.Region == Row.Region;

    /// <summary>Shows a newer version of the row without the list having to be built again.</summary>
    public void Show(MappingRow row)
    {
        _showing = true;
        try
        {
            Row = row;
            FoundBrush = BrushOf(row.Found);
            FoundText = Strings.Format(Strings.MappingColour, Capitalised(row.FoundName.ToEnglish()), row.FoundHex);
            RegionText = row.Region == RegionPolarity.Light ? Strings.MappingOnLight : Strings.MappingOnDark;
            ShareText = Strings.Format(Strings.PercentValue, row.SharePercent.ToString(CultureInfo.CurrentCulture));
            SelectedRole = Roles.FirstOrDefault(choice => choice.Role == row.Role);
            ShownBrush = BrushOf(row.Shown);
            ShownText = Strings.Format(Strings.MappingColour, Capitalised(row.ShownName.ToEnglish()), row.ShownHex);
            string contrast = row.ContrastAgainstPage is { } ratio
                ? Strings.Format(Strings.MappingContrast, ratio.ToString("0.0", CultureInfo.CurrentCulture))
                : string.Empty;
            ShownDetail = (contrast, row.Pinned) switch
            {
                ({ Length: > 0 }, true) => contrast + ", " + Strings.MappingPinned,
                (_, true) => Strings.MappingPinned,
                _ => contrast,
            };
            Spoken = Strings.Format(Strings.MappingRowName, FoundText, ShareText, SelectedRole?.Name ?? string.Empty, ShownText);
            RoleListName = Strings.Format(Strings.MappingRoleOf, FoundText);
        }
        finally
        {
            _showing = false;
        }
    }

    partial void OnSelectedRoleChanged(RoleChoice? value)
    {
        if (!_showing && value is not null && value.Role != Row.Role)
        {
            _pin(Row, value.Role);
        }
    }

    private static string Capitalised(string name) =>
        name.Length == 0 ? name : char.ToUpper(name[0], CultureInfo.CurrentCulture) + name[1..];

    private static SolidColorBrush BrushOf(Rgb8 colour)
    {
        var brush = new SolidColorBrush(Color.FromRgb(colour.R, colour.G, colour.B));
        brush.Freeze();
        return brush;
    }
}
