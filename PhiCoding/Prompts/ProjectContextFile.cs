namespace PhiCoding.Prompts;

/// <summary>
/// A single project-instructions file (typically <c>AGENTS.md</c>) included
/// in the system prompt under <c>&lt;project_context&gt;</c>.
/// </summary>
public sealed record ProjectContextFile(string AbsolutePath, string Content);
