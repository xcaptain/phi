namespace PhiCoding.ToolCards;

/// <summary>
/// Built-in tool metadata registry. The single source of truth for
/// <c>ToolDescriptor</c> values for the four built-in tool kinds; unknown
/// tool names (e.g. MCP tools) fall through to <see cref="Generic"/> via
/// <see cref="For"/>.
/// </summary>
public static class ToolDescriptors
{
    private static readonly ToolDescriptor Read = new(ToolKind.Read, "read", "📄");
    private static readonly ToolDescriptor Write = new(ToolKind.Write, "write", "📝");
    private static readonly ToolDescriptor Edit = new(ToolKind.Edit, "edit", "✏️");
    private static readonly ToolDescriptor Bash = new(ToolKind.Bash, "bash", "🐚");
    private static readonly ToolDescriptor Generic = new(ToolKind.Generic, "tool", "🔧");

    /// <summary>
    /// Returns the descriptor for <paramref name="toolName"/>. Unknown names
    /// map to <see cref="Generic"/> so renderers never have to special-case
    /// null.
    /// </summary>
    public static ToolDescriptor For(string toolName) => toolName switch
    {
        "read" => Read,
        "write" => Write,
        "edit" => Edit,
        "bash" => Bash,
        _ => Generic,
    };

    /// <summary>Built-in descriptors in canonical order, useful for tests.</summary>
    public static IReadOnlyList<ToolDescriptor> All { get; } = [Read, Write, Edit, Bash, Generic];
}