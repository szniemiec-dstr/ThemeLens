// SPDX-License-Identifier: GPL-3.0-or-later
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ThemeLens.Core.Colour;

namespace ThemeLens.Core.Themes;

/// <summary>Thrown when a theme file is not valid. The message is meant to be shown to the person who wrote the file.</summary>
public sealed class ThemeFormatException : Exception
{
    public ThemeFormatException(string message)
        : base(message)
    {
    }

    public ThemeFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Reads and writes theme files (section 5.5). The format is described by <c>themes/theme-1.schema.json</c>;
/// this class applies the same rules without needing a schema library at run time.
/// </summary>
public static class ThemeJson
{
    public const string FileExtension = ".theme.json";

    /// <summary>Relative reference used by the built-in files so editors find the schema next to them.</summary>
    public const string SchemaReference = "./theme-1.schema.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public static Theme Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        ThemeFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ThemeFile>(json, Options);
        }
        catch (JsonException e)
        {
            throw new ThemeFormatException($"The theme file is not valid: {e.Message}", e);
        }

        if (file is null)
        {
            throw new ThemeFormatException("The theme file is empty.");
        }

        return ToTheme(file);
    }

    public static Theme Load(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (ThemeFormatException e)
        {
            throw new ThemeFormatException($"{Path.GetFileName(path)}: {e.Message}", e);
        }
    }

    public static string Serialize(Theme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        var file = new ThemeFile
        {
            Schema = SchemaReference,
            Id = theme.Id,
            Name = theme.Name,
            Description = theme.Description,
            Base = theme.Base,
            Roles = theme.Roles is { } r
                ? new RolesFile
                {
                    Page = r.Page.ToHex(),
                    Panel = r.Panel.ToHex(),
                    Text = r.Text.ToHex(),
                    QuietText = r.QuietText.ToHex(),
                    Link = r.Link.ToHex(),
                    Good = r.Good.ToHex(),
                    Warning = r.Warning.ToHex(),
                    Bad = r.Bad.ToHex(),
                }
                : null,
            Limits = new LimitsFile
            {
                MaxLuminance = theme.Limits.MaxLuminance,
                MinTextContrast = theme.Limits.MinTextContrast,
                MaxChroma = theme.Limits.MaxChroma,
                MaxAreaContrast = theme.Limits.MaxAreaContrast,
            },
            Warmth = theme.Warmth,
            ColourVision = theme.ColourVision,
            KeepBackground = theme.KeepBackground,
            Contrast = new ContrastFile { Amount = theme.Contrast.Amount, Mode = theme.Contrast.Mode },
            Sharpen = new SharpenFile { Amount = theme.Sharpen.Amount, Mode = theme.Sharpen.Mode },
        };
        return JsonSerializer.Serialize(file, Options) + "\n";
    }

    private static Theme ToTheme(ThemeFile file)
    {
        string id = Require(file.Id, "id");
        if (id.Length is 0 or > 64 || !id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'))
        {
            throw new ThemeFormatException("\"id\" must be 1 to 64 characters from a-z, 0-9 and '-'.");
        }

        string name = Require(file.Name, "name");
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80)
        {
            throw new ThemeFormatException("\"name\" must be 1 to 80 characters.");
        }

        ThemeBase themeBase = file.Base ?? throw new ThemeFormatException("\"base\" is required: \"dark\", \"light\" or \"keep\".");

        ThemeRoles? roles = null;
        if (themeBase == ThemeBase.Keep)
        {
            if (file.Roles is not null)
            {
                throw new ThemeFormatException("A theme with base \"keep\" leaves backgrounds alone and must not define \"roles\".");
            }
        }
        else
        {
            RolesFile r = file.Roles ?? throw new ThemeFormatException($"A theme with base \"{Lower(themeBase)}\" must define \"roles\".");
            roles = new ThemeRoles(
                Colour(r.Page, "roles.page"),
                Colour(r.Panel, "roles.panel"),
                Colour(r.Text, "roles.text"),
                Colour(r.QuietText, "roles.quietText"),
                Colour(r.Link, "roles.link"),
                Colour(r.Good, "roles.good"),
                Colour(r.Warning, "roles.warning"),
                Colour(r.Bad, "roles.bad"));

            bool pageIsDark = Wcag.Luminance(roles.Page) < Wcag.Luminance(roles.Text);
            if (pageIsDark != (themeBase == ThemeBase.Dark))
            {
                throw new ThemeFormatException(
                    themeBase == ThemeBase.Dark
                        ? "On a dark base \"roles.page\" must be darker than \"roles.text\"."
                        : "On a light base \"roles.page\" must be lighter than \"roles.text\".");
            }
        }

        LimitsFile lf = file.Limits ?? new LimitsFile();
        var limits = new ThemeLimits
        {
            MaxLuminance = InRange(lf.MaxLuminance ?? 1f, 0.02f, 1f, "limits.maxLuminance"),
            MinTextContrast = InRange(lf.MinTextContrast ?? 4.5f, 1f, 21f, "limits.minTextContrast"),
            MaxChroma = lf.MaxChroma is { } c ? InRange(c, 0f, 0.5f, "limits.maxChroma") : null,
            MaxAreaContrast = InRange(lf.MaxAreaContrast ?? 6f, 1.5f, 21f, "limits.maxAreaContrast"),
        };

        return new Theme
        {
            Id = id,
            Name = name,
            Description = file.Description,
            Base = themeBase,
            Roles = roles,
            Limits = limits,
            Warmth = InRange(file.Warmth ?? 0f, 0f, 1f, "warmth"),
            ColourVision = file.ColourVision ?? ColourVisionMode.None,
            KeepBackground = file.KeepBackground
                ?? (themeBase == ThemeBase.Keep ? KeepBackgroundMode.Off : KeepBackgroundMode.Strict),
            Contrast = new ContrastSettings(
                InRange(file.Contrast?.Amount ?? 0f, 0f, 1f, "contrast.amount"),
                file.Contrast?.Mode ?? ContrastMode.Standard),
            Sharpen = new SharpenSettings(
                InRange(file.Sharpen?.Amount ?? 0f, 0f, 1f, "sharpen.amount"),
                file.Sharpen?.Mode ?? SharpenMode.Standard),
        };
    }

    private static string Lower(ThemeBase b) => b.ToString().ToLowerInvariant();

    private static string Require(string? value, string name) =>
        value ?? throw new ThemeFormatException($"\"{name}\" is required.");

    private static Rgb8 Colour(string? value, string name)
    {
        if (value is null)
        {
            throw new ThemeFormatException($"\"{name}\" is required.");
        }

        return Rgb8.TryParseHex(value, out Rgb8 colour)
            ? colour
            : throw new ThemeFormatException($"\"{name}\" must be a colour in the form #RRGGBB, not \"{value}\".");
    }

    private static float InRange(float value, float min, float max, string name) =>
        float.IsFinite(value) && value >= min && value <= max
            ? value
            : throw new ThemeFormatException(FormattableString.Invariant($"\"{name}\" must be between {min} and {max}."));

    private sealed class ThemeFile
    {
        [JsonPropertyName("$schema")]
        [JsonPropertyOrder(-1)]
        public string? Schema { get; set; }

        public string? Id { get; set; }

        public string? Name { get; set; }

        public string? Description { get; set; }

        public ThemeBase? Base { get; set; }

        public RolesFile? Roles { get; set; }

        public LimitsFile? Limits { get; set; }

        public float? Warmth { get; set; }

        public ColourVisionMode? ColourVision { get; set; }

        public KeepBackgroundMode? KeepBackground { get; set; }

        public ContrastFile? Contrast { get; set; }

        public SharpenFile? Sharpen { get; set; }
    }

    private sealed class RolesFile
    {
        public string? Page { get; set; }

        public string? Panel { get; set; }

        public string? Text { get; set; }

        public string? QuietText { get; set; }

        public string? Link { get; set; }

        public string? Good { get; set; }

        public string? Warning { get; set; }

        public string? Bad { get; set; }
    }

    private sealed class LimitsFile
    {
        public float? MaxLuminance { get; set; }

        public float? MinTextContrast { get; set; }

        public float? MaxChroma { get; set; }

        public float? MaxAreaContrast { get; set; }
    }

    private sealed class ContrastFile
    {
        public float? Amount { get; set; }

        public ContrastMode? Mode { get; set; }
    }

    private sealed class SharpenFile
    {
        public float? Amount { get; set; }

        public SharpenMode? Mode { get; set; }
    }
}
