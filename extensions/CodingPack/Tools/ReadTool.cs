using System.ComponentModel;
using Phi.Agent;
using Phi.Extensions.CodingPack.Tools.Details;

namespace Phi.Extensions.CodingPack.Tools;

public sealed record ReadArgs
{
    [Description("Path to the file to read")]
    public required string Path { get; init; }

    [Description("Optional 1-indexed line number to start reading from (defaults to 1)")]
    public int? Offset { get; init; }

    [Description("Optional maximum number of lines to read (defaults to the rest of the file)")]
    public int? Limit { get; init; }
}

public sealed partial class ReadTool : TypedTool<ReadArgs>
{
    /// <summary>
    /// Hard upper bound on lines returned in a single call. Defends against
    /// the model passing a huge <c>limit</c> by accident; the response
    /// includes a continuation hint pointing at the next <c>offset</c>.
    /// </summary>
    internal const int MaxLinesPerCall = 2000;

    private readonly IWorkspacePathResolver? _resolver;

    public override string Name => "read";

    public override string Description =>
        "Read the contents of a text file. Supports optional 1-indexed `offset` " +
        "and positive integer `limit` arguments to slice the file. For large files, " +
        "use offset/limit to read a window at a time and increment offset to continue. " +
        "When `offset` is null it defaults to line 1; when `limit` is null it reads " +
        "to end of file. Relative paths resolve against the session working directory.";

    /// <summary>Creates a read tool that resolves relative paths against <paramref name="cwd"/>.</summary>
    public ReadTool(string cwd) : this(new WorkspacePathResolver(cwd)) { }

    /// <summary>Creates a read tool that resolves relative paths through <paramref name="resolver"/>.</summary>
    public ReadTool(IWorkspacePathResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Creates a read tool that only handles absolute paths and otherwise
    /// uses the process working directory. Kept for backward compatibility.
    /// </summary>
    public ReadTool() { }

    public override async Task<ToolResult> ExecuteTypedAsync(ReadArgs args, CancellationToken cancellationToken)
    {
        var path = _resolver?.Resolve(args.Path) ?? args.Path;
        try
        {
            if (Directory.Exists(path))
                return new ToolResult(
                    [new TextBlock($"Path is a directory, not a file: {path}")],
                    IsError: true);

            if (!File.Exists(path))
                return new ToolResult(
                    [new TextBlock($"File not found: {path}")],
                    IsError: true);

            if (args.Offset is { } off && off < 1)
                return new ToolResult(
                    [new TextBlock($"offset must be at least 1 (got {off})")],
                    IsError: true);

            if (args.Limit is { } lim && lim < 1)
                return new ToolResult(
                    [new TextBlock($"limit must be at least 1 (got {lim})")],
                    IsError: true);

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            var allLines = content.Replace("\r\n", "\n").Split('\n');
            var totalLines = allLines.Length;

            // Convert 1-indexed offset to 0-indexed start; null/0 → first line.
            var startLine = args.Offset is { } o && o > 0 ? o - 1 : 0;

            if (startLine >= totalLines)
            {
                return new ToolResult(
                    [new TextBlock(
                        $"offset {args.Offset} is beyond end of file ({totalLines} lines total)")],
                    IsError: true);
            }

            var requestedLimit = args.Limit;
            var effectiveLimit = requestedLimit is null
                ? totalLines - startLine
                : Math.Min(requestedLimit.Value, totalLines - startLine);
            var truncatedByLimit = requestedLimit is not null
                && startLine + effectiveLimit < totalLines;

            var endLine = startLine + effectiveLimit;
            var selected = string.Join('\n', allLines[startLine..endLine]);
            var truncatedByHardCap = effectiveLimit > MaxLinesPerCall;
            if (truncatedByHardCap)
            {
                selected = string.Join('\n', allLines[startLine..(startLine + MaxLinesPerCall)]);
                endLine = startLine + MaxLinesPerCall;
                effectiveLimit = MaxLinesPerCall;
            }

            var returnedLines = effectiveLimit;
            var displayStart = startLine + 1;
            var displayEnd = startLine + returnedLines;
            var hasMore = endLine < totalLines || truncatedByLimit || truncatedByHardCap;
            string? hint = null;
            if (hasMore)
            {
                var nextOffset = endLine + 1;
                if (truncatedByHardCap)
                    hint = $"\n\n[Output truncated at {MaxLinesPerCall} lines. Use offset={nextOffset} to continue.]";
                else
                    hint = $"\n\n[{totalLines - endLine} more lines in file. Use offset={nextOffset} to continue.]";
            }

            var output = string.IsNullOrEmpty(selected) ? "(empty slice)" : selected;
            if (hint is not null) output += hint;

            var details = new ReadDetails(
                Path: path,
                Offset: displayStart,
                Limit: returnedLines,
                LineCount: returnedLines,
                TotalLineCount: totalLines,
                ByteCount: System.Text.Encoding.UTF8.GetByteCount(content));
            return new ToolResult(
                [new TextBlock(output)],
                Details: ToolDetails.Node(details));
        }
        catch (UnauthorizedAccessException)
        {
            return new ToolResult(
                [new TextBlock($"Permission denied reading: {path}")],
                IsError: true);
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"Error reading {path}: {ex.Message}")],
                IsError: true);
        }
    }
}
