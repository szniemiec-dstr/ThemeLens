// SPDX-License-Identifier: GPL-3.0-or-later
using System.IO;

namespace ThemeLens.App;

/// <summary>
/// Where the app keeps what it remembers: its settings and the user's own themes (section 5.5). Normally that
/// is <c>%AppData%\ThemeLens</c>. The environment variable <c>THEMELENS_DATA</c> names another folder, which
/// is how the UI tests give every run a clean slate and how a copy on a USB stick can keep its settings with it.
/// </summary>
internal static class AppPaths
{
    public const string DataVariable = "THEMELENS_DATA";

    public static string DataDirectory { get; } = Resolve();

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static string ThemesDirectory => Path.Combine(DataDirectory, "themes");

    private static string Resolve()
    {
        string? chosen = Environment.GetEnvironmentVariable(DataVariable);
        return string.IsNullOrWhiteSpace(chosen)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ThemeLens")
            : Path.GetFullPath(chosen);
    }
}
