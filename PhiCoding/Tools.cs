using System.Diagnostics;
using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiCoding;

public sealed class BashTool
{
    public ToolDefinition Definition { get; } = new(
        Name: "bash",
        Description: "Run a shell command and return stdout/stderr/exit code.",
        Parameters: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["command"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Shell command to execute",
                },
            },
            ["required"] = new JsonArray { "command" },
        },
        PromptSnippet: "Execute shell commands",
        PromptGuidelines: ["Prefer the read tool over cat for inspecting files."]);

    public async Task<ToolResult> ExecuteAsync(
        string toolCallId,
        JsonNode arguments,
        CancellationToken cancellationToken)
    {
        var command = arguments["command"]?.GetValue<string>() ?? "";

        var psi = new ProcessStartInfo("/bin/bash", ["-c", command])
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

public sealed class ReadTool
{
    public ToolDefinition Definition { get; } = new(
        Name: "read",
        Description: "Read the contents of a text file at the given path.",
        Parameters: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Path to the file to read",
                },
            },
            ["required"] = new JsonArray { "path" },
        },
        PromptSnippet: "Read file contents",
        PromptGuidelines: ["Use read to examine files instead of cat or sed."]);

    public async Task<ToolResult> ExecuteAsync(
        string toolCallId,
        JsonNode arguments,
        CancellationToken cancellationToken)
    {
        var path = arguments["path"]?.GetValue<string>() ?? "";

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

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            return new ToolResult([new TextBlock(content)]);
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

public sealed class WriteTool
{
    public ToolDefinition Definition { get; } = new(
        Name: "write",
        Description: "Write content to a file at the given path, overwriting if it exists. Creates parent directories as needed.",
        Parameters: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "File path to write to",
                },
                ["content"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Content to write to the file",
                },
            },
            ["required"] = new JsonArray { "path", "content" },
        },
        PromptSnippet: "Write file contents");

    public async Task<ToolResult> ExecuteAsync(
        string toolCallId,
        JsonNode arguments,
        CancellationToken cancellationToken)
    {
        var path = arguments["path"]?.GetValue<string>() ?? "";
        var content = arguments["content"]?.GetValue<string>() ?? "";

        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.WriteAllTextAsync(path, content, cancellationToken);
            return new ToolResult(
                [new TextBlock($"Wrote {content.Length} chars to {path}")]);
        }
        catch (UnauthorizedAccessException)
        {
            return new ToolResult(
                [new TextBlock($"Permission denied writing to: {path}")],
                IsError: true);
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"Error writing {path}: {ex.Message}")],
                IsError: true);
        }
    }
}

public sealed class EditTool
{
    public ToolDefinition Definition { get; } = new(
        Name: "edit",
        Description: "Find old_string in the file and replace it with new_string. The old_string must appear exactly once.",
        Parameters: new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["path"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "File path to edit",
                },
                ["old_string"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Exact string to find (must be unique in the file)",
                },
                ["new_string"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "Replacement string",
                },
            },
            ["required"] = new JsonArray { "path", "old_string", "new_string" },
        },
        PromptSnippet: "Surgical edits",
        PromptGuidelines: ["old_string must be unique — include surrounding context if it's not."]);

    public async Task<ToolResult> ExecuteAsync(
        string toolCallId,
        JsonNode arguments,
        CancellationToken cancellationToken)
    {
        var path = arguments["path"]?.GetValue<string>() ?? "";
        var oldString = arguments["old_string"]?.GetValue<string>() ?? "";
        var newString = arguments["new_string"]?.GetValue<string>() ?? "";

        try
        {
            if (!File.Exists(path))
                return new ToolResult(
                    [new TextBlock($"File not found: {path}")],
                    IsError: true);

            var content = await File.ReadAllTextAsync(path, cancellationToken);
            var occurrences = CountOccurrences(content, oldString);

            if (occurrences == 0)
                return new ToolResult(
                    [new TextBlock($"old_string not found in {path}")],
                    IsError: true);

            if (occurrences > 1)
                return new ToolResult(
                    [new TextBlock($"old_string appears {occurrences} times in {path} — must be unique")],
                    IsError: true);

            var newContent = content.Replace(oldString, newString);
            await File.WriteAllTextAsync(path, newContent, cancellationToken);

            return new ToolResult(
                [new TextBlock($"Edited {path} ({oldString.Length} → {newString.Length} chars)")]);
        }
        catch (UnauthorizedAccessException)
        {
            return new ToolResult(
                [new TextBlock($"Permission denied editing: {path}")],
                IsError: true);
        }
        catch (Exception ex)
        {
            return new ToolResult(
                [new TextBlock($"Error editing {path}: {ex.Message}")],
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