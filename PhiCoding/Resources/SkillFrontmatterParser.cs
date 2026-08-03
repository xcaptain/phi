namespace PhiCoding.Resources;

/// <summary>
/// Minimal YAML-like frontmatter parser for skill <c>SKILL.md</c> files.
/// Mirrors tau's <c>parse_markdown_resource</c>: only simple
/// <c>key: value</c> pairs are supported, no nested structures, no escape
/// handling. The body is everything after the closing <c>---</c>.
/// </summary>
public static class SkillFrontmatterParser
{
    private const string OpenMarker = "---";
    private const string CloseMarker = "---";

    /// <summary>
    /// Result of parsing one <c>SKILL.md</c>. <see cref="Name"/> is null
    /// when the frontmatter was unparseable; callers should treat that as
    /// a load failure. <see cref="Body"/> is the entire file content when
    /// no frontmatter is present.
    /// </summary>
    public sealed record ParseResult(
        string? Name,
        string? Description,
        string Body,
        IReadOnlyList<ResourceDiagnostic> Diagnostics);

    public static ParseResult Parse(string skillName, string filePath, string content)
    {
        ArgumentException.ThrowIfNullOrEmpty(skillName);
        ArgumentNullException.ThrowIfNull(content);

        var normalized = content.Replace("\r\n", "\n");
        var diagnostics = new List<ResourceDiagnostic>();

        if (!StartsWithFrontmatterOpen(normalized))
        {
            // No frontmatter: whole content is body, name falls back to
            // directory name, description is empty.
            return new ParseResult(skillName, null, content, diagnostics);
        }

        var afterOpen = normalized.IndexOf('\n') + 1;
        var closeStart = normalized.IndexOf("\n---", afterOpen, StringComparison.Ordinal);
        if (closeStart < 0)
        {
            diagnostics.Add(new ResourceDiagnostic(
                Source: $"SKILL.md:{filePath}",
                Message: $"Skill '{skillName}' has unterminated frontmatter (missing closing '---'); skipping.",
                Severity: DiagnosticSeverity.Warning));
            return new ParseResult(null, null, content, diagnostics);
        }

        var fmText = normalized[afterOpen..closeStart];
        var bodyStart = closeStart + 1 + CloseMarker.Length; // skip "\n---"
        var body = bodyStart < normalized.Length ? normalized[bodyStart..] : "";
        if (body.StartsWith('\n')) body = body[1..];

        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in fmText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            var sep = trimmed.IndexOf(':');
            if (sep <= 0) continue;
            var key = trimmed[..sep].Trim();
            var value = trimmed[(sep + 1)..].Trim().Trim('"', '\'');
            map[key] = value;
        }

        var name = skillName;
        if (map.TryGetValue("name", out var declaredName) && !string.IsNullOrWhiteSpace(declaredName))
            name = declaredName;
        else
        {
            diagnostics.Add(new ResourceDiagnostic(
                Source: $"SKILL.md:{filePath}",
                Message: $"Skill '{skillName}' has no 'name' in frontmatter; using directory name.",
                Severity: DiagnosticSeverity.Warning));
        }

        var description = map.TryGetValue("description", out var declaredDesc)
            ? declaredDesc
            : null;

        return new ParseResult(name, description, body, diagnostics);
    }

    /// <summary>
    /// Returns the body of a <c>SKILL.md</c> with the frontmatter block removed
    /// and leading/trailing whitespace trimmed — what gets injected when a
    /// skill is invoked (frontmatter is index metadata, not instructions).
    /// Falls back to the whole (trimmed) content when there is no parseable
    /// frontmatter block.
    /// </summary>
    public static string StripFrontmatter(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalized = content.Replace("\r\n", "\n");
        if (!StartsWithFrontmatterOpen(normalized)) return content.Trim();

        var afterOpen = normalized.IndexOf('\n') + 1;
        var closeStart = normalized.IndexOf("\n---", afterOpen, StringComparison.Ordinal);
        if (closeStart < 0) return content.Trim();

        var bodyStart = closeStart + 1 + CloseMarker.Length; // skip "\n---"
        var body = bodyStart < normalized.Length ? normalized[bodyStart..] : "";
        if (body.StartsWith('\n')) body = body[1..];
        return body.Trim();
    }

    private static bool StartsWithFrontmatterOpen(string text) =>
        text.StartsWith(OpenMarker + "\n", StringComparison.Ordinal)
        || string.Equals(text, OpenMarker, StringComparison.Ordinal);
}
