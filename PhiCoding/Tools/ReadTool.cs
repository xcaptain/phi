using System.ComponentModel;
using PhiAgent;

namespace PhiCoding.Tools;

public sealed record ReadArgs
{
    [Description("Path to the file to read")]
    public required string Path { get; init; }
}

public sealed class ReadTool : TypedTool<ReadArgs>
{
    public override string Name => "read";
    public override string Description => "Read the contents of a text file at the given path.";

    public override async Task<ToolResult> ExecuteTypedAsync(ReadArgs args, CancellationToken cancellationToken)
    {
        try
        {
            if (Directory.Exists(args.Path))
                return new ToolResult(
                    [new TextBlock($"Path is a directory, not a file: {args.Path}")],
                    IsError: true);

            if (!File.Exists(args.Path))
                return new ToolResult(
                    [new TextBlock($"File not found: {args.Path}")],
                    IsError: true);

            var content = await File.ReadAllTextAsync(args.Path, cancellationToken);
            return new ToolResult([new TextBlock(content)]);
        }
        catch (UnauthorizedAccessException)
        {
            return new ToolResult(
                [new TextBlock($"Permission denied reading: {args.Path}")],
                IsError: true);
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"Error reading {args.Path}: {ex.Message}")],
                IsError: true);
        }
    }
}