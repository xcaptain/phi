using System.ComponentModel;
using PhiAgent;

namespace PhiCoding.Tools;

public sealed record WriteArgs
{
    [Description("File path to write to")]
    public required string Path { get; init; }

    [Description("Content to write to the file")]
    public required string Content { get; init; }
}

public sealed class WriteTool : TypedTool<WriteArgs>
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

            await File.WriteAllTextAsync(args.Path, args.Content, cancellationToken);
            return new ToolResult(
                [new TextBlock($"Wrote {args.Content.Length} chars to {args.Path}")]);
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