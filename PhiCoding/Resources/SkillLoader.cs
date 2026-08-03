using System.Text;
using PhiCoding.Prompts;

namespace PhiCoding.Resources;

/// <summary>
/// Discovers Agent Skills on disk: walks the user-level
/// (<c>~/.agents/skills</c>) and project-level
/// (<c>&lt;projectRoot&gt;/.agents/skills</c>) directories, parses each
/// skill's <c>SKILL.md</c> frontmatter, and returns the resulting
/// descriptors + diagnostics.
/// <para>
/// Project skills override user skills of the same name (the project
/// version wins, with an informational diagnostic). Individual files are
/// capped at <see cref="MaxFileSizeBytes"/>; missing or malformed
/// frontmatter produces warnings, not load failures — a missing
/// <c>description</c> is recoverable (the skill still appears, with an
/// empty description), while a missing or broken <c>name</c> falls back to
/// the directory name.
/// </para>
/// </summary>
public static class SkillLoader
{
    public const int MaxFileSizeBytes = 64 * 1024;
    public const string SkillFileName = "SKILL.md";
    private const string AgentsSubdir = ".agents";
    private const string SkillsSubdir = "skills";

    /// <summary>
    /// Result of one <see cref="Load"/> call. <see cref="Skills"/> is
    /// ordered by source (project first, then user) and then by name, so
    /// the system prompt can render a stable list.
    /// </summary>
    public sealed record LoadResult(
        IReadOnlyList<SkillDescriptor> Skills,
        IReadOnlyList<ResourceDiagnostic> Diagnostics);

    public static LoadResult Load(SkillLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var cwd = Path.GetFullPath(options.Cwd);
        var projectRoot = ProjectRootLocator.Locate(cwd);
        var homeDir = options.HomeDir
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var diagnostics = new List<ResourceDiagnostic>();
        var byName = new Dictionary<string, SkillDescriptor>(StringComparer.OrdinalIgnoreCase);
        var bySource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // name -> source label

        if (!string.IsNullOrEmpty(homeDir))
            LoadFromDir(
                Path.Combine(homeDir, AgentsSubdir, SkillsSubdir),
                source: "user",
                byName, bySource, diagnostics);

        if (projectRoot is not null)
            LoadFromDir(
                Path.Combine(projectRoot, AgentsSubdir, SkillsSubdir),
                source: "project",
                byName, bySource, diagnostics);

        var ordered = byName.Values
            .OrderBy(s => s.Source, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToArray();
        return new LoadResult(ordered, diagnostics);
    }

    private static void LoadFromDir(
        string dir,
        string source,
        Dictionary<string, SkillDescriptor> byName,
        Dictionary<string, string> bySource,
        List<ResourceDiagnostic> diagnostics)
    {
        if (!Directory.Exists(dir)) return;

        foreach (var skillDir in Directory.EnumerateDirectories(dir))
        {
            var skillName = Path.GetFileName(skillDir);
            var skillFile = Path.Combine(skillDir, SkillFileName);
            if (!File.Exists(skillFile)) continue;

            try
            {
                var info = new FileInfo(skillFile);
                if (info.Length > MaxFileSizeBytes)
                {
                    diagnostics.Add(new ResourceDiagnostic(
                        Source: $"SKILL.md:{skillFile}",
                        Message: $"Skill '{skillName}' is {info.Length} bytes, exceeding the {MaxFileSizeBytes}-byte limit; skipped.",
                        Severity: DiagnosticSeverity.Warning));
                    continue;
                }

                var content = File.ReadAllText(skillFile, Encoding.UTF8);
                var parse = SkillFrontmatterParser.Parse(skillName, skillFile, content);
                diagnostics.AddRange(parse.Diagnostics);
                if (parse.Name is null) continue; // unparseable frontmatter — skip

                var descriptor = new SkillDescriptor
                {
                    Name = parse.Name,
                    Description = parse.Description ?? "",
                    AbsolutePath = skillFile,
                    Source = source,
                };

                if (byName.TryGetValue(skillName, out var existing))
                {
                    var previousSource = bySource[skillName];
                    if (!previousSource.Equals(source, StringComparison.Ordinal))
                    {
                        diagnostics.Add(new ResourceDiagnostic(
                            Source: $"SKILL.md:{skillFile}",
                            Message: $"Skill '{skillName}' from {source} overrides the same skill from {previousSource}.",
                            Severity: DiagnosticSeverity.Info));
                    }
                }

                byName[skillName] = descriptor;
                bySource[skillName] = source;
            }
            catch (Exception ex)
            {
                diagnostics.Add(new ResourceDiagnostic(
                    Source: $"SKILL.md:{skillFile}",
                    Message: $"Failed to read skill '{skillName}': {ex.Message}",
                    Severity: DiagnosticSeverity.Warning));
            }
        }
    }
}
