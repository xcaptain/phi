using System.ComponentModel;
using PhiAgent;
using PhiCoding.Tools.Details;

namespace PhiCoding.Tools;

public sealed record WriteArgs
{
    [Description("File path to write to")]
    public required string Path { get; init; }

    [Description("Content to write to the file")]
    public required string Content { get; init; }
}

public sealed partial class WriteTool : TypedTool<WriteArgs>
{
    public override string Name => "write";
    public override string Description =>
        "Write content to a file at the given path, overwriting if it exists. Creates parent directories as needed.";

    public override async Task<ToolResult> ExecuteTypedAsync(WriteArgs args, CancellationToken cancellationToken)
    {
        try
        {
            var dir = Path.GetDirectoryName(args.Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var existed = File.Exists(args.Path);
            await File.WriteAllTextAsync(args.Path, args.Content, cancellationToken);

            var details = new WriteDetails(
                Path: args.Path,
                BytesWritten: System.Text.Encoding.UTF8.GetByteCount(args.Content),
                Mode: existed ? "overwrote" : "created");
            return new ToolResult(
                [new TextBlock($"Wrote {args.Content.Length} chars to {args.Path} ({details.Mode})")],
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
