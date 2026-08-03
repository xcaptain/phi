namespace PhiCoding.Prompts;

/// <summary>
/// Metadata for a skill discovered on disk. Only the index fields are
/// included in the system prompt; the body of <c>SKILL.md</c> is read on
/// demand by the model via the <c>read</c> tool.
/// </summary>
public sealed record SkillDescriptor(
    string Name,
    string Description,
    string AbsolutePath);
