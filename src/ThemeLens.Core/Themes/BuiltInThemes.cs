// SPDX-License-Identifier: GPL-3.0-or-later
using System.Reflection;

namespace ThemeLens.Core.Themes;

/// <summary>
/// The themes that ship with the app. The files live in <c>themes/</c> at the repository root and are embedded
/// into this assembly, so they cannot go missing from an install.
/// </summary>
public static class BuiltInThemes
{
    private const string ResourcePrefix = "ThemeLens.Themes.";

    /// <summary>Order in which the themes are offered, grouped by base as in section 5.3.</summary>
    private static readonly string[] Order =
    [
        "soft-dark", "high-contrast-dark", "red-green-assist-dark", "night-warm",
        "clear-white", "soft-paper", "high-contrast-light", "evening-paper",
        "passthrough", "red-green-assist", "less-blue", "greyscale", "dimmer",
    ];

    private static readonly Lazy<IReadOnlyList<Theme>> Loaded = new(LoadAll);

    public static IReadOnlyList<Theme> All => Loaded.Value;

    public const string DefaultDarkId = "soft-dark";
    public const string DefaultLightId = "clear-white";
    public const string DefaultKeepId = "passthrough";

    public static Theme ById(string id) =>
        All.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"There is no built-in theme with the id '{id}'.");

    public static Theme? FindByName(string name) =>
        All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public static IEnumerable<Theme> ForBase(ThemeBase themeBase) => All.Where(t => t.Base == themeBase);

    public static Theme DefaultFor(ThemeBase themeBase) => ById(themeBase switch
    {
        ThemeBase.Dark => DefaultDarkId,
        ThemeBase.Light => DefaultLightId,
        _ => DefaultKeepId,
    });

    private static IReadOnlyList<Theme> LoadAll()
    {
        Assembly assembly = typeof(BuiltInThemes).Assembly;
        var themes = new List<Theme>();
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(ResourcePrefix, StringComparison.Ordinal)
                || !resource.EndsWith(ThemeJson.FileExtension, StringComparison.Ordinal))
            {
                continue;
            }

            using Stream stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            try
            {
                themes.Add(ThemeJson.Parse(reader.ReadToEnd()));
            }
            catch (ThemeFormatException e)
            {
                throw new InvalidOperationException($"Built-in theme '{resource}' is invalid: {e.Message}", e);
            }
        }

        int Rank(Theme t)
        {
            int i = Array.IndexOf(Order, t.Id);
            return i < 0 ? int.MaxValue : i;
        }

        return themes.OrderBy(Rank).ThenBy(t => t.Id, StringComparer.Ordinal).ToArray();
    }
}
