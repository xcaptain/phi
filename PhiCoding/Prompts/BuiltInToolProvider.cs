using PhiCoding.Tools;

namespace PhiCoding.Prompts;

/// <summary>
/// Contributes the four built-in tools (<c>bash</c>, <c>read</c>,
/// <c>write</c>, <c>edit</c>) with their prompt snippets, guidelines and
/// capabilities. Cwd binding is added in a later phase; the first version
/// exposes metadata only.
/// </summary>
public sealed class BuiltInToolProvider
{
    public static IReadOnlyList<ToolContribution> GetTools() =>
    [
        new()
        {
            Tool = new BashTool(),
            PromptSnippet = "bash: Run a shell command and return stdout, stderr and exit code.",
            PromptGuidelines =
            [
                "Use bash for shell inspection and running commands.",
                "Do not use bash to read file contents — prefer read.",
            ],
            Capabilities = ToolCapabilities.ExecuteCommands,
        },
        new()
        {
            Tool = new ReadTool(),
            PromptSnippet = "read: Read a file from the local workspace, with optional offset/limit.",
            PromptGuidelines =
            [
                "Use read to inspect files before editing them.",
                "For large files, use offset and limit on read to read a slice at a time and increment offset to continue. Do not use cat, sed, or head to read files.",
            ],
            Capabilities = ToolCapabilities.ReadLocalFiles,
        },
        new()
        {
            Tool = new WriteTool(),
            PromptSnippet = "write: Create a new file or overwrite an existing file.",
            PromptGuidelines =
            [
                "Use write for new files or full rewrites.",
            ],
            Capabilities = ToolCapabilities.WriteLocalFiles,
        },
        new()
        {
            Tool = new EditTool(),
            PromptSnippet = "edit: Make a surgical edit to an existing file (old_string must be unique).",
            PromptGuidelines =
            [
                "Use edit for surgical changes (old_string must be unique).",
            ],
            Capabilities = ToolCapabilities.WriteLocalFiles,
        },
    ];
}