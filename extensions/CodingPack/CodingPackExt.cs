using Phi.Agent;
using Phi.Extensions.CodingPack.Tools;

namespace Phi.Extensions.CodingPack;

/// <summary>
/// The coding pack: Phi's first "real" extension and the Sprint 2.5 proof
/// that the extension system can carry the core coding capability.
/// <list type="bullet">
/// <item>Registers the four built-in tools (<c>bash</c>, <c>read</c>,
/// <c>write</c>, <c>edit</c>) via <see cref="IPhiApi.RegisterTool"/>.</item>
/// <item>Injects the coding behavior rules via
/// <see cref="IPhiApi.AddPromptGuideline"/>.</item>
/// </list>
/// <para>
/// Sprint 2.5 note: Phi.Tui / Phi.Avalonia reference this assembly at
/// compile time (not via dll discovery) so the default coding capability
/// is always present — the same shape as any other extension, but shipped
/// in the box. The <c>FileOpsExtractor</c> compaction helper stays in the
/// Phi core because the compaction pipeline (which runs there) needs it at
/// build time and Phi can't reference CodingPack (would be a cycle); the
/// tool-name coupling it encodes is slated for removal in Sprint 4.
/// </para>
/// </summary>
[PhiExtension(
    Name = "coding-pack",
    Version = "1.0.0",
    Description = "Default coding tools (bash, read, write, edit) + coding system prompt.",
    Capabilities = ExtensionCapability.FileSystemRead
        | ExtensionCapability.FileSystemWrite
        | ExtensionCapability.ProcessSpawn)]
public sealed class CodingPackExt : IPhiExtension
{
    public void Setup(IPhiApi api)
    {
        var cwd = api.Context.Cwd;

        api.RegisterTool(new BashTool(cwd), BashContribution(cwd));
        api.RegisterTool(new ReadTool(cwd), ReadContribution(cwd));
        api.RegisterTool(new WriteTool(cwd), WriteContribution(cwd));
        api.RegisterTool(new EditTool(cwd), EditContribution(cwd));

        api.AddPromptGuideline(
            "You are an expert coding assistant. Inspect the workspace with read before " +
            "modifying; use bash for shell inspection and running commands; use edit for " +
            "surgical changes and write for new files or full rewrites.");

        // /tools is a live demonstration of the extension slash-command
        // dispatcher: typing /tools in the TUI runs this handler directly
        // (no LLM round-trip) and shows the result as a transient line.
        api.RegisterCommand(
            "/tools",
            (args, _) => args.Length == 0
                ? "coding-pack tools: bash, read, write, edit"
                : $"coding-pack tools ({args}): bash, read, write, edit",
            description: "List the coding-pack tools.",
            usage: "/tools [filter]");
    }

    private static ToolContribution BashContribution(string cwd) => new()
    {
        Tool = new BashTool(cwd),
        PromptSnippet = "bash: Run a shell command and return stdout, stderr and exit code.",
        PromptGuidelines =
        [
            "Use bash for shell inspection and running commands.",
            "Do not use bash to read file contents — prefer read.",
        ],
        Capabilities = ToolCapabilities.ExecuteCommands,
    };

    private static ToolContribution ReadContribution(string cwd) => new()
    {
        Tool = new ReadTool(cwd),
        PromptSnippet = "read: Read a file from the local workspace, with optional offset/limit.",
        PromptGuidelines =
        [
            "Use read to inspect files before editing them.",
            "For large files, use offset and limit on read to read a slice at a time and increment offset to continue. Do not use cat, sed, or head to read files.",
        ],
        Capabilities = ToolCapabilities.ReadLocalFiles,
    };

    private static ToolContribution WriteContribution(string cwd) => new()
    {
        Tool = new WriteTool(cwd),
        PromptSnippet = "write: Create a new file or overwrite an existing file.",
        PromptGuidelines =
        [
            "Use write for new files or full rewrites.",
        ],
        Capabilities = ToolCapabilities.WriteLocalFiles,
    };

    private static ToolContribution EditContribution(string cwd) => new()
    {
        Tool = new EditTool(cwd),
        PromptSnippet = "edit: Make a surgical edit to an existing file (old_string must be unique).",
        PromptGuidelines =
        [
            "Use edit for surgical changes (old_string must be unique).",
        ],
        Capabilities = ToolCapabilities.WriteLocalFiles,
    };
}
