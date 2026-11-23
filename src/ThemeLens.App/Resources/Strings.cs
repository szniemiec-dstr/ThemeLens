// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Resources;

namespace ThemeLens.App;

/// <summary>
/// Every text the app shows, read from <c>Strings.resx</c> so that it can be translated (NFR-08). XAML refers
/// to these with <c>{x:Static app:Strings.Name}</c>; nothing user-visible is written in code or markup.
/// </summary>
public static class Strings
{
    private static readonly ResourceManager Resources = new("ThemeLens.App.Strings", typeof(Strings).Assembly);

    public static string AppTitle => Get();

    public static string LensLabel => Get();

    public static string LensOn => Get();

    public static string LensOff => Get();

    public static string LensSwitchName => Get();

    public static string ThemeLabel => Get();

    public static string BaseDark => Get();

    public static string BaseLight => Get();

    public static string BaseKeep => Get();

    public static string FillScreen => Get();

    public static string EditMode => Get();

    public static string EditModeHelp => Get();

    public static string StatusLabel => Get();

    public static string StatusOff => Get();

    public static string StatusLive => Get();

    public static string StatusLiveReduced => Get();

    public static string StatusHolding => Get();

    public static string StatusEditing => Get();

    public static string SlowStrongSharpen => Get();

    public static string SlowLocalContrast => Get();

    public static string SlowCpuRendering => Get();

    public static string FrameRate => Get();

    public static string FrameRateName => Get();

    public static string SoftwareRendering => Get();

    public static string DevModeNote => Get();

    public static string DevModeHeading => Get();

    public static string HandleTextDev => Get();

    public static string PatternNote => Get();

    public static string CaptureNote => Get();

    public static string ShortcutsHeading => Get();

    public static string ShortcutGesture => Get();

    public static string ShortcutNotSet => Get();

    public static string ShortcutToggleLens => Get();

    public static string ShortcutEditMode => Get();

    public static string ShortcutFillScreen => Get();

    public static string ShortcutNextTheme => Get();

    public static string ShortcutControlWindow => Get();

    public static string ShortcutEverythingOff => Get();

    public static string ShortcutRowName => Get();

    public static string AdjustHeading => Get();

    public static string BrightnessLabel => Get();

    public static string WarmthLabel => Get();

    public static string ContrastLabel => Get();

    public static string SharpenLabel => Get();

    public static string KeepBackgroundLabel => Get();

    public static string KeepStrict => Get();

    public static string KeepBalanced => Get();

    public static string KeepOff => Get();

    public static string ResetAdjustments => Get();

    public static string PercentValue => Get();

    public static string MappingHeading => Get();

    public static string MappingEmpty => Get();

    public static string MappingFound => Get();

    public static string MappingShare => Get();

    public static string MappingUsedAs => Get();

    public static string MappingShownAs => Get();

    public static string MappingColour => Get();

    public static string MappingContrast => Get();

    public static string MappingOnLight => Get();

    public static string MappingOnDark => Get();

    public static string MappingPinned => Get();

    public static string MappingRowName => Get();

    public static string MappingRoleOf => Get();

    public static string ResetMapping => Get();

    public static string RolePage => Get();

    public static string RolePanel => Get();

    public static string RoleText => Get();

    public static string RoleQuietText => Get();

    public static string RoleLink => Get();

    public static string RoleGood => Get();

    public static string RoleWarning => Get();

    public static string RoleBad => Get();

    public static string RoleOtherAccent => Get();

    public static string SaveAsTheme => Get();

    public static string NewThemeName => Get();

    public static string SaveThemeFailed => Get();

    public static string HandleText => Get();

    public static string StartFailed => Get();

    public static string CloseButton => Get();

    public static string UnknownOption => Get();

    /// <summary>Fills the placeholders of a text in the user's culture.</summary>
    public static string Format(string text, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, text, arguments);

    private static string Get([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
