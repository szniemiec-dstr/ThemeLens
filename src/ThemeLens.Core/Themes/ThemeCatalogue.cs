// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text;

namespace ThemeLens.Core.Themes;

/// <summary>A theme file that could not be loaded, and why: shown to the user instead of being silently skipped.</summary>
public sealed record ThemeLoadProblem(string File, string Message);

/// <summary>
/// The themes on offer: the built-in ones plus the user's own from a folder
/// (<c>%AppData%\ThemeLens\themes</c> on Windows). Also saves the current settings as a named theme (FR-15).
/// </summary>
public sealed class ThemeCatalogue
{
    private readonly string? _userDirectory;
    private List<Theme> _userThemes = [];
    private List<ThemeLoadProblem> _problems = [];

    /// <param name="userDirectory">Folder of the user's theme files, or null for built-in themes only.</param>
    public ThemeCatalogue(string? userDirectory)
    {
        _userDirectory = userDirectory;
        Reload();
    }

    public IReadOnlyList<Theme> BuiltIn => BuiltInThemes.All;

    public IReadOnlyList<Theme> User => _userThemes;

    /// <summary>User theme files that were refused, with a message for their author.</summary>
    public IReadOnlyList<ThemeLoadProblem> Problems => _problems;

    public IEnumerable<Theme> All => BuiltIn.Concat(User);

    public Theme? Find(string id) => All.FirstOrDefault(t => t.Id == id);

    public bool IsBuiltIn(string id) => BuiltIn.Any(t => t.Id == id);

    /// <summary>Reads the user folder again.</summary>
    public void Reload()
    {
        var themes = new List<Theme>();
        var problems = new List<ThemeLoadProblem>();
        if (_userDirectory is not null && Directory.Exists(_userDirectory))
        {
            foreach (string file in Directory.GetFiles(_userDirectory, "*" + ThemeJson.FileExtension).Order(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Theme theme = ThemeJson.Load(file);
                    if (IsBuiltIn(theme.Id) || themes.Any(t => t.Id == theme.Id))
                    {
                        problems.Add(new ThemeLoadProblem(Path.GetFileName(file), $"Another theme already uses the id \"{theme.Id}\"."));
                        continue;
                    }

                    themes.Add(theme);
                }
                catch (Exception e) when (e is ThemeFormatException or IOException or UnauthorizedAccessException)
                {
                    problems.Add(new ThemeLoadProblem(Path.GetFileName(file), e.Message));
                }
            }
        }

        _userThemes = themes;
        _problems = problems;
    }

    /// <summary>
    /// Saves <paramref name="settings"/> as a user theme called <paramref name="name"/> and returns it. An
    /// existing user theme with the same name is replaced; built-in themes are never overwritten.
    /// </summary>
    public Theme SaveAs(Theme settings, string name)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_userDirectory is null)
        {
            throw new InvalidOperationException("This catalogue has no folder for user themes.");
        }

        name = name.Trim();
        if (name.Length is 0 or > 80)
        {
            throw new ArgumentException("A theme name has 1 to 80 characters.", nameof(name));
        }

        string id = User.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))?.Id ?? FreeId(Slug(name));

        // The file is what-you-see: its role colours are the colours shown at its warmth.
        Theme theme = Rebase(settings) with { Id = id, Name = name, Description = null };
        Directory.CreateDirectory(_userDirectory);
        string path = Path.Combine(_userDirectory, id + ThemeJson.FileExtension);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, ThemeJson.Serialize(theme), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporary, path, overwrite: true);
        Reload();
        return Find(id) ?? theme;
    }

    /// <summary>Deletes a user theme. Returns false for built-in or unknown themes.</summary>
    public bool Delete(string id)
    {
        if (_userDirectory is null || IsBuiltIn(id) || User.All(t => t.Id != id))
        {
            return false;
        }

        File.Delete(Path.Combine(_userDirectory, id + ThemeJson.FileExtension));
        Reload();
        return true;
    }

    /// <summary>
    /// Rewrites role colours so that they are the colours shown at the theme's current warmth, and makes that
    /// warmth the authored one.
    /// </summary>
    private static Theme Rebase(Theme theme)
    {
        if (theme.Roles is not { } roles || theme.Warmth == theme.AuthoredWarmth)
        {
            return theme with { AuthoredWarmth = theme.Warmth };
        }

        Colour.Rgb8 Shown(Colour.Rgb8 colour) => Mapping.TableBuilder.ShownAtCurrentWarmth(theme, colour);
        return theme with
        {
            Roles = new ThemeRoles(
                Shown(roles.Page), Shown(roles.Panel), Shown(roles.Text), Shown(roles.QuietText),
                Shown(roles.Link), Shown(roles.Good), Shown(roles.Warning), Shown(roles.Bad)),
            AuthoredWarmth = theme.Warmth,
        };
    }

    private string FreeId(string wanted)
    {
        string id = wanted;
        for (int n = 2; Find(id) is not null; n++)
        {
            id = wanted + "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return id;
    }

    /// <summary>Turns a name into an id of a-z, 0-9 and '-'.</summary>
    public static string Slug(string name)
    {
        var id = new StringBuilder();
        foreach (char c in name.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                id.Append(c);
            }
            else if (id.Length > 0 && id[^1] != '-' && (char.IsWhiteSpace(c) || c is '-' or '_' or '–' or '.'))
            {
                id.Append('-');
            }
        }

        string result = id.ToString().Trim('-');
        if (result.Length > 60)
        {
            result = result[..60].Trim('-');
        }

        return result.Length == 0 ? "theme" : result;
    }
}
