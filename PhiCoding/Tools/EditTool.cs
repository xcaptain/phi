using System.ComponentModel;
using PhiAgent;

namespace PhiCoding.Tools;

public sealed record EditArgs
{
    [Description("File path to edit")]
    public required string Path { get; init; }

    [Description("Exact string to find (must be unique in the file)")]
    public required string OldString { get; init; }

    [Description("Replacement string")]
    public required string NewString { get; init; }
}

public sealed class EditTool : TypedTool<EditArgs>
{
    public override string Name => "edit";
    public override string Description =>
        "Find old_string in the file and replace it with new_string. The old_string must appear exactly once.";

    public override async Task<ToolResult> ExecuteTypedAsync(EditArgs args, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(args.Path))
                return new ToolResult(
                    [new TextBlock($"File not found: {args.Path}")],
                    IsError: true);

            var content = await File.ReadAllTextAsync(args.Path, cancellationToken);
            var occurrences = CountOccurrences(content, args.OldString);

            if (occurrences == 0)
                return new ToolResult(
                    [new TextBlock($"old_string not found in {args.Path}")],
                    IsError: true);

            if (occurrences > 1)
                return new ToolResult(
                    [new TextBlock($"old_string appears {occurrences} times in {args.Path} — must be unique")],
                    IsError: true);

            var newContent = content.Replace(args.OldString, args.NewString);
            await File.WriteAllTextAsync(args.Path, newContent, cancellationToken);

            return new ToolResult(
                [new TextBlock($"Edited {args.Path} ({args.OldString.Length} → {args.NewString.Length} chars)")]);
        }
        catch (UnauthorizedAccessException)
        {
            return new ToolResult(
                [new TextBlock($"Permission denied editing: {args.Path}")],
                IsError: true);
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"Error editing {args.Path}: {ex.Message}")],
                IsError: true);
        }
    }

    private static int CountOccurrences(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}