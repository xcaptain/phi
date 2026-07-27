using System.ComponentModel;
using System.Diagnostics;
using PhiAgent;

namespace PhiCoding;

public sealed record BashArgs
{
    [Description("Shell command to execute")]
    public required string Command { get; init; }
}

public sealed class BashTool : TypedTool<BashArgs>
{
    public override string Name => "bash";
    public override string Description => "Run a shell command and return stdout/stderr/exit code.";
    public override string? PromptSnippet => "Execute shell commands";
    public override IReadOnlyList<string>? PromptGuidelines =>
        ["Prefer the read tool over cat for inspecting files."];

    public override async Task<ToolResult> ExecuteTypedAsync(BashArgs args, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo("/bin/bash", ["-c", args.Command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(psi)!;
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);

        var content = new List<ContentBlock>();
        if (!string.IsNullOrEmpty(stdout))
            content.Add(new TextBlock(stdout));
        if (!string.IsNullOrEmpty(stderr))
            content.Add(new TextBlock("[stderr] " + stderr));
        if (content.Count == 0)
            content.Add(new TextBlock($"<no output, exit={process.ExitCode}>"));

        return new ToolResult(content, IsError: process.ExitCode != 0);
    }
}

public sealed record ReadArgs
{
    [Description("Path to the file to read")]
    public required string Path { get; init; }
}

public sealed class ReadTool : TypedTool<ReadArgs>
{
    public override string Name => "read";
    public override string Description => "Read the contents of a text file at the given path.";
    public override string? PromptSnippet => "Read file contents";
    public override IReadOnlyList<string>? PromptGuidelines =>
        ["Use read to examine files instead of cat or sed."];

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
    public override string? PromptSnippet => "Write file contents";

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
    public override string? PromptSnippet => "Surgical edits";
    public override IReadOnlyList<string>? PromptGuidelines =>
        ["old_string must be unique — include surrounding context if it's not."];

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