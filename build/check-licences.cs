// SPDX-License-Identifier: GPL-3.0-or-later
//
// Checks that every NuGet package a shipped project resolves carries a licence on the allow-list of section
// 12.2 of the specification, and writes THIRD-PARTY-NOTICES.md from what it finds (NFR-09).
//
//   dotnet run build/check-licences.cs -- [--write] <project folder>...
//
// Run "dotnet restore" first: the tool reads obj/project.assets.json of each project and the .nuspec files in
// the NuGet package cache. It needs no network access. Without --write it only checks, and fails when the
// committed notices file is out of date.

using System.Text;
using System.Text.Json;
using System.Xml.Linq;

string[] allowed = ["MIT", "BSD-2-Clause", "BSD-3-Clause", "Apache-2.0", "ISC", "Zlib", "0BSD"];

// Packages whose .nuspec carries a licence file or URL instead of an SPDX expression. Each entry is a recorded
// decision: the licence was read by a person. Add to this list deliberately, never to make the build pass.
var recorded = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    // The .NET runtime, WPF and the Windows SDK projection are all MIT (github.com/dotnet).
    ["Microsoft.Windows.SDK.NET.Ref"] = "MIT",
    ["Microsoft.WindowsDesktop.App.Ref"] = "MIT",
    ["Microsoft.NETCore.App.Ref"] = "MIT",
};

bool write = args.Contains("--write");
string[] projects = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
if (projects.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run build/check-licences.cs -- [--write] <project folder>...");
    return 2;
}

var packages = new SortedDictionary<string, (string Version, string Licence, string? Url)>(StringComparer.OrdinalIgnoreCase);
var buildOnly = new SortedDictionary<string, (string Version, string Licence, string? Url)>(StringComparer.OrdinalIgnoreCase);
var problems = new List<string>();

foreach (string project in projects)
{
    string assetsPath = Path.Combine(project, "obj", "project.assets.json");
    if (!File.Exists(assetsPath))
    {
        problems.Add($"{project}: no obj/project.assets.json. Run 'dotnet restore' first.");
        continue;
    }

    using JsonDocument assets = JsonDocument.Parse(File.ReadAllText(assetsPath));
    string[] folders = assets.RootElement.GetProperty("packageFolders").EnumerateObject().Select(f => f.Name).ToArray();

    // A package is shipped when it puts something into the output folder. Source generators and the metadata
    // they read are used at build time only; the specification checks build tools separately (section 12.2).
    var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (JsonProperty target in assets.RootElement.GetProperty("targets").EnumerateObject())
    {
        foreach (JsonProperty entry in target.Value.EnumerateObject())
        {
            bool Ships(string group) =>
                entry.Value.TryGetProperty(group, out JsonElement files)
                && files.EnumerateObject().Any(f => !f.Name.EndsWith("_._", StringComparison.Ordinal));
            if (Ships("runtime") || Ships("native") || Ships("runtimeTargets") || Ships("resource"))
            {
                shipped.Add(entry.Name);
            }
        }
    }

    foreach (JsonProperty library in assets.RootElement.GetProperty("libraries").EnumerateObject())
    {
        if (library.Value.GetProperty("type").GetString() != "package")
        {
            continue;
        }

        string[] idVersion = library.Name.Split('/');
        string id = idVersion[0];
        string version = idVersion[1];
        string relative = library.Value.GetProperty("path").GetString()!;
        string? nuspec = folders
            .Select(f => Path.Combine(f, relative, id.ToLowerInvariant() + ".nuspec"))
            .FirstOrDefault(File.Exists);
        if (nuspec is null)
        {
            problems.Add($"{id} {version}: package not found in the NuGet cache.");
            continue;
        }

        XElement metadata = XDocument.Load(nuspec).Root!.Elements().First(e => e.Name.LocalName == "metadata");
        XElement? licence = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "license");
        string? url = metadata.Elements().FirstOrDefault(e => e.Name.LocalName == "projectUrl")?.Value;
        string? expression = licence?.Attribute("type")?.Value == "expression" ? licence.Value.Trim() : null;
        if (expression is null && recorded.TryGetValue(id, out string? known))
        {
            expression = known;
        }

        if (!shipped.Contains(library.Name))
        {
            buildOnly[id] = (version, expression ?? "see the package", url);
            continue;
        }

        if (expression is null)
        {
            problems.Add($"{id} {version}: no SPDX licence expression in the package and no recorded decision.");
            continue;
        }

        if (!IsAllowed(expression))
        {
            problems.Add($"{id} {version}: licence '{expression}' is not on the allow-list ({string.Join(", ", allowed)}).");
        }

        packages[id] = (version, expression, url);
    }
}

var notices = new StringBuilder();
notices.AppendLine("# Third-party notices");
notices.AppendLine();
notices.AppendLine("ThemeLens is shipped with the components below. This file is generated by");
notices.AppendLine("`build/check-licences.cs` from the resolved package graph of the shipped projects; do not edit it by hand.");
notices.AppendLine("Every licence here is on the allow-list of section 12.2 of the specification.");
notices.AppendLine();
notices.AppendLine("The .NET runtime and WPF, which are published together with the app, are licensed under the MIT licence");
notices.AppendLine("by the .NET Foundation and contributors.");
notices.AppendLine();
notices.AppendLine("| Component | Version | Licence | Project |");
notices.AppendLine("| --- | --- | --- | --- |");
foreach ((string id, (string version, string licence, string? url)) in packages)
{
    notices.AppendLine($"| {id} | {version} | {licence} | {url} |");
}

if (packages.Count == 0)
{
    notices.AppendLine("| (no third-party packages) | | | |");
}

notices.AppendLine();
notices.AppendLine("## Used to build the shipped projects, not shipped");
notices.AppendLine();
notices.AppendLine("| Component | Version | Licence | Project |");
notices.AppendLine("| --- | --- | --- | --- |");
foreach ((string id, (string version, string licence, string? url)) in buildOnly)
{
    notices.AppendLine($"| {id} | {version} | {licence} | {url} |");
}

if (buildOnly.Count == 0)
{
    notices.AppendLine("| (none) | | | |");
}

notices.AppendLine();
notices.AppendLine("Packages used only by the test projects are not listed here.");

string noticesPath = Path.Combine(FindRoot(), "THIRD-PARTY-NOTICES.md");
string text = notices.ToString().ReplaceLineEndings("\n");
if (write)
{
    File.WriteAllText(noticesPath, text);
    Console.WriteLine($"Wrote {noticesPath} ({packages.Count} packages).");
}
else if (!File.Exists(noticesPath) || File.ReadAllText(noticesPath).ReplaceLineEndings("\n") != text)
{
    problems.Add("THIRD-PARTY-NOTICES.md is out of date. Run this tool with --write and commit the result.");
}

foreach (string problem in problems)
{
    Console.Error.WriteLine("error: " + problem);
}

Console.WriteLine(problems.Count == 0 ? $"Licence check passed: {packages.Count} shipped packages." : $"Licence check failed: {problems.Count} problem(s).");
return problems.Count == 0 ? 0 : 1;

bool IsAllowed(string expression)
{
    // "A OR B": one acceptable alternative is enough. "A AND B": every part has to be acceptable.
    return expression
        .Split(" OR ", StringSplitOptions.TrimEntries)
        .Any(alternative => alternative
            .Trim('(', ')')
            .Split(" AND ", StringSplitOptions.TrimEntries)
            .All(part => allowed.Contains(part.Trim('(', ')'), StringComparer.OrdinalIgnoreCase)));
}

static string FindRoot()
{
    for (DirectoryInfo? d = new(Directory.GetCurrentDirectory()); d is not null; d = d.Parent)
    {
        if (File.Exists(Path.Combine(d.FullName, "ThemeLens.slnx")))
        {
            return d.FullName;
        }
    }

    return Directory.GetCurrentDirectory();
}
