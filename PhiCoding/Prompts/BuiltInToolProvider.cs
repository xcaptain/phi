using PhiCoding.Tools;

namespace PhiCoding.Prompts;

/// <summary>
/// Contributes the four built-in tools (<c>bash</c>, <c>read</c>,
/// <c>write</c>, <c>edit</c>) with their prompt snippets, guidelines and
/// capabilities. Each tool is bound to the supplied working directory.
/// </summary>
public sealed class BuiltInToolProvider
{
    private readonly string _cwd;

    public BuiltInToolProvider(string cwd)
    {
        ArgumentException.ThrowIfNullOrEmpty(cwd);
        _cwd = cwd;
    }

    public IReadOnlyList<ToolContribution> GetTools() =>
    [
        new()
        {
            Tool = new BashTool(_cwd),
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
            Tool = new ReadTool(_cwd),
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
            Tool = new WriteTool(_cwd),
            PromptSnippet = "write: Create a new file or overwrite an existing file.",
            PromptGuidelines =
            [
                "Use write for new files or full rewrites.",
            ],
            Capabilities = ToolCapabilities.WriteLocalFiles,
        },
        new()
        {
            Tool = new EditTool(_cwd),
            PromptSnippet = "edit: Make a surgical edit to an existing file (old_string must be unique).",
            PromptGuidelines =
            [
                "Use edit for surgical changes (old_string must be unique).",
            ],
            Capabilities = ToolCapabilities.WriteLocalFiles,
        },
    ];
}