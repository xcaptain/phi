using System.Text.RegularExpressions;

namespace Phi.Resources;

/// <summary>
/// Validates skill frontmatter metadata against the Agent Skills standard,
/// mirroring pi: name/description violations produce warnings but the skill
/// still loads, while a missing/empty <c>description</c> is the single fatal
/// case. Unknown frontmatter fields are ignored (no warning) by design.
/// </summary>
public static partial class SkillValidator
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 1024;

    private static readonly Regex NamePattern = MyRegex();

    /// <summary>Warning messages for a skill name. Empty when valid.</summary>
    public static IReadOnlyList<string> ValidateName(string name)
    {
        var errors = new List<string>();
        if (name.Length > MaxNameLength)
            errors.Add($"name exceeds {MaxNameLength} characters ({name.Length})");
        if (!NamePattern.IsMatch(name))
            errors.Add("name contains invalid characters (must be lowercase a-z, 0-9, hyphens only)");
        if (name.StartsWith('-') || name.EndsWith('-'))
            errors.Add("name must not start or end with a hyphen");
        if (name.Contains("--"))
            errors.Add("name must not contain consecutive hyphens");
        return errors;
    }

    /// <summary>
    /// Warning messages for a skill description. A missing/empty description
    /// reports <c>"description is required"</c>; the loader treats that as the
    /// one fatal case and skips the skill.
    /// </summary>
    public static IReadOnlyList<string> ValidateDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return ["description is required"];
        return description.Length > MaxDescriptionLength
            ? [$"description exceeds {MaxDescriptionLength} characters ({description.Length})"]
            : [];
    }

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
