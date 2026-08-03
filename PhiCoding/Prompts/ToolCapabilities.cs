namespace PhiCoding.Prompts;

/// <summary>
/// Capability flags advertised by a <see cref="ToolContribution"/>. The
/// system-prompt builder consults these instead of relying on tool naming
/// conventions (e.g. <c>tool.Name == "read"</c>) so future MCP tools can
/// participate in the same gating logic.
/// </summary>
[Flags]
public enum ToolCapabilities
{
    None = 0,
    ReadLocalFiles = 1 << 0,
    WriteLocalFiles = 1 << 1,
    ExecuteCommands = 1 << 2,
}