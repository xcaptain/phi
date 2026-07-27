using System.Diagnostics;
using System.Text.Json.Nodes;
using PhiAgent;

namespace PhiCoding;

/// <summary>
/// Runs shell commands via /bin/bash. Concrete implementation lives in
/// the application layer; the PhiAgent library has no opinion on how
/// individual tools execute.
/// </summary>
public sealed class BashTool
{
    public Tool Definition { get; } = new(
        Name: "bash",
        Description: "Run a shell command and return stdout/stderr/exit code.",
        Parameters: new Dictionary<string, JsonNode>
        {
            ["type"] = JsonValue.Create("object"),
            ["properties"] = JsonNode.Parse("""
                {"command":{"type":"string","description":"Shell command to execute"}}
                """),
            ["required"] = JsonNode.Parse("""["command"]"""),
        });

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