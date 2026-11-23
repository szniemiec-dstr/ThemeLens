// SPDX-License-Identifier: GPL-3.0-or-later
using System.Globalization;
using System.Resources;

namespace ThemeLens.Platform.Windows;

/// <summary>
/// The few texts this layer shows to the user, all about why capture is not available. They come from
/// <c>PlatformStrings.resx</c>, ready for translation (NFR-08).
/// </summary>
internal static class PlatformStrings
{
    private static readonly ResourceManager Resources =
        new("ThemeLens.Platform.Windows.PlatformStrings", typeof(PlatformStrings).Assembly);

    public static string CaptureBlocked => Get(nameof(CaptureBlocked));

    public static string CaptureUnsupported => Get(nameof(CaptureUnsupported));

    public static string LensCannotBeExcluded => Get(nameof(LensCannotBeExcluded));

    public static string CaptureFailed => Get(nameof(CaptureFailed));

    public static string CaptureRotated => Get(nameof(CaptureRotated));

    public static string Format(string format, params object?[] arguments) =>
        string.Format(CultureInfo.CurrentCulture, format, arguments);

    private static string Get(string name) => Resources.GetString(name, CultureInfo.CurrentUICulture) ?? name;
}
