using System.ComponentModel;
using Phi.Agent;
using Phi.Extensions.CodingPack.Tools.Details;

namespace Phi.Extensions.CodingPack.Tools;

public sealed record WriteArgs
{
    [Description("File path to write to")]
    public required string Path { get; init; }

    [Description("Content to write to the file")]
    public required string Content { get; init; }
}

public sealed partial class WriteTool : TypedTool<WriteArgs>
{
    private readonly IWorkspacePathResolver? _resolver;

    public override string Name => "write";
    public override string Description =>
        "Write content to a file at the given path, overwriting if it exists. " +
        "Creates parent directories as needed. Relative paths resolve against " +
        "the session working directory.";

    /// <summary>Creates a write tool bound to <paramref name="cwd"/>.</summary>
    public WriteTool(string cwd) : this(new WorkspacePathResolver(cwd)) { }

    /// <summary>Creates a write tool bound to <paramref name="resolver"/>.</summary>
    public WriteTool(IWorkspacePathResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Creates a write tool that uses the process working directory. Kept
    /// for backward compatibility.
    /// </summary>
    public WriteTool() { }

    public override async Task<ToolResult> ExecuteTypedAsync(WriteArgs args, CancellationToken cancellationToken)
    {
        try
        {
            var path = _resolver?.Resolve(args.Path) ?? args.Path;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var existed = File.Exists(path);
            await File.WriteAllTextAsync(path, args.Content, cancellationToken);

            var details = new WriteDetails(
                Path: path,
                BytesWritten: System.Text.Encoding.UTF8.GetByteCount(args.Content),
                Mode: existed ? "overwrote" : "created");
            return new ToolResult(
                [new TextBlock($"Wrote {args.Content.Length} chars to {path} ({details.Mode})")],
                Details: ToolDetails.Node(details));
        }
        catch (UnauthorizedAccessException)
        {
            return new ToolResult(
                [new TextBlock($"Permission denied writing to: {args.Path}")],
                IsError: true);
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"Error writing {args.Path}: {ex.Message}")],
                IsError: true);
        }
    }
}
