namespace Phi.Prompts;

/// <summary>
/// Metadata for a skill discovered on disk. Only the index fields are
/// included in the system prompt; the body of <c>SKILL.md</c> is read on
/// demand by the model via the <c>read</c> tool when the user invokes
/// <c>/skill:NAME</c>.
/// <para>
/// <see cref="Source"/> is a string label (<c>"user"</c>, <c>"project"</c>)
/// so the system prompt can render the source next to each entry. The
/// loader keeps project skills ahead of user skills when both exist.
/// </para>
/// </summary>
public sealed record SkillDescriptor
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string AbsolutePath { get; init; }
    public string Source { get; init; } = "user";
}
